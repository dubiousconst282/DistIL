namespace DistIL.Passes;

using DistIL.IR;
using DistIL.Analysis;

//SSA transform implementation based on the standard dominance frontier algorithm.
public class SsaTransform : MethodPass
{
    MethodBody _method = null!;
    Dictionary<PhiInst, Variable> _phiDefs = new(); //phi -> variable

    public override void Run(MethodTransformContext ctx)
    {
        _method = ctx.Method;
        var domTree = ctx.GetAnalysis<DominatorTree>(preserve: true);
        var domFrontier = ctx.GetAnalysis<DominanceFrontier>(preserve: true);

        InsertPhis(domFrontier);
        RenameDefs(domTree);
        PrunePhis();

        _phiDefs.Clear();
    }

    private void InsertPhis(DominanceFrontier domFrontier)
    {
        var varDefs = new Dictionary<Variable, (bool Global, ArrayStack<BasicBlock> Wl)>(); //var -> blocks assigning to var
        var killedVars = new ValueSet<Variable>();

        //Find variable definitions
        foreach (var block in _method) {
            foreach (var inst in block) {
                if (inst is StoreVarInst store && CanEnreg(store)) {
                    var worklist = varDefs.GetOrAddRef(store.Var).Wl ??= new();
                    //Add parent block to the worklist, avoiding dupes
                    if (worklist.Count == 0 || worklist.Top != block) {
                        worklist.Push(block);
                    }
                    killedVars.Add(store.Var);
                }
                //If we are loading a variable that has not yet been assigned in this block, mark it as global
                else if (inst is LoadVarInst load && CanEnreg(load) && !killedVars.Contains(load.Var)) {
                    varDefs.GetOrAddRef(load.Var).Global = true;
                }
            }
            killedVars.Clear();
        }

        var phiAdded = new ValueSet<BasicBlock>(); //blocks where a phi has been added
        var processed = new ValueSet<BasicBlock>(); //blocks already visited in worklist

        //Insert phis
        foreach (var (variable, (isGlobal, worklist)) in varDefs) {
            //Avoid inserting phis for variables only alive in a single block (semi-pruned ssa)
            if (worklist == null || !isGlobal) continue;

            //Initialize processed set (we do this to avoid keeping a whole HashSet for each variable)
            foreach (var def in worklist) {
                processed.Add(def);
            }
            //Recursively insert phis on the DF of each block in the worklist
            while (worklist.TryPop(out var block)) {
                foreach (var dom in domFrontier.Of(block)) {
                    if (!phiAdded.Add(dom)) continue;
                    
                    var phi = dom.AddPhi(variable.ResultType);
                    _phiDefs.Add(phi, variable);

                    if (processed.Add(dom)) {
                        worklist.Push(dom);
                    }
                }
            }
            phiAdded.Clear();
            processed.Clear();
        }
    }

    private void RenameDefs(DominatorTree domTree)
    {
        //TODO: Push once per block (would need another dictionary, may not be worth)
        var defStacks = new Dictionary<Variable, ArrayStack<Value>>();
        var defDeltas = new ArrayStack<(BasicBlock B, Variable V)>();
        defDeltas.Push((null!, null!)); //dummy element so we don't need to check IsEmpty in RestoreDefs

        domTree.Traverse(
            preVisit: RenameBlock,
            postVisit: RestoreDefs
        );

        void RenameBlock(BasicBlock block)
        {
            //Init phi defs
            foreach (var phi in block.Phis()) {
                if (_phiDefs.TryGetValue(phi, out var variable)) {
                    PushDef(block, variable, phi);
                }
            }
            foreach (var inst in block.NonPhis()) {
                //Update latest def
                if (inst is StoreVarInst store && CanEnreg(store)) {
                    PushDef(block, store.Var, store.Value);
                    store.Remove();
                }
                //Replace load with latest def
                else if (inst is LoadVarInst load && CanEnreg(load)) {
                    var currDef = ReadDef(load.Var);
                    load.ReplaceWith(currDef);
                }
            }
            //Fill successors phis
            foreach (var succ in block.Succs) {
                foreach (var phi in succ.Phis()) {
                    if (_phiDefs.TryGetValue(phi, out var variable)) {
                        var currDef = ReadDef(variable);
                        //TODO: AddArg() is O(n), maybe rewrite all phis in a final pass
                        phi.AddArg(block, currDef);
                    }
                }
            }
        }
        void RestoreDefs(BasicBlock block)
        {
            //Restore def stack to what it was before visiting `block`
            while (defDeltas.Top.B == block) {
                defStacks[defDeltas.Top.V].Pop();
                defDeltas.Pop();
            }
        }
        //Helpers for R/W the def stack
        void PushDef(BasicBlock block, Variable var, Value def)
        {
            var stack = defStacks.GetOrAddRef(var) ??= new();
            stack.Push(def);
            defDeltas.Push((block, var));
        }
        Value ReadDef(Variable var)
        {
            var stack = defStacks.GetValueOrDefault(var);
            return stack != null && !stack.IsEmpty 
                ? stack.Top 
                : new Undef(var.ResultType);
        }
    }

    private static bool CanEnreg(VarAccessInst inst)
    {
        //We don't have a way to represent metadata in the IR currently, so don't enreg pinned variables.
        return inst.Var is { IsExposed: false, IsPinned: false };
    }

    private void PrunePhis()
    {
        //Algorithm from the SSABook
        var usefulPhis = new ValueSet<PhiInst>();
        var propagationStack = new ArrayStack<PhiInst>();
        var pruneablePhis = new List<PhiInst>();
        //Initial marking phase
        foreach (var block in _method) {
            foreach (var phi in block.Phis()) {
                //Remove phis with the same value in all args
                if (IsTrivialPhi(phi)) {
                    phi.ReplaceWith(phi.GetValue(0), false);
                }
                //Enqueue phis with dependencies from non-phi instructions
                else if (HasStrongDependencies(phi)) {
                    propagationStack.Push(phi);
                }
                //This phi is not considered useful yet, enqueue for possible removal
                else {
                    pruneablePhis.Add(phi);
                }
            }
        }
        //Propagate usefulness
        while (propagationStack.TryPop(out var phi)) {
            for (int i = 0; i < phi.NumArgs; i++) {
                if (phi.GetValue(i) is PhiInst otherPhi && usefulPhis.Add(otherPhi)) {
                    propagationStack.Push(otherPhi);
                }
            }
        }
        //Prune useless phis
        foreach (var phi in pruneablePhis) {
            if (!usefulPhis.Contains(phi)) {
                phi.Remove();
            }
        }
        
        static bool IsTrivialPhi(PhiInst phi)
        {
            var value = phi.GetValue(0);
            for (int i = 1; i < phi.NumArgs; i++) {
                if (phi.GetValue(i) != value) {
                    return false;
                }
            }
            return true;
        }
        static bool HasStrongDependencies(PhiInst phi)
        {
            foreach (var user in phi.Users()) {
                if (user is not PhiInst) {
                    return true;
                }
            }
            return false;
        }
    }
}