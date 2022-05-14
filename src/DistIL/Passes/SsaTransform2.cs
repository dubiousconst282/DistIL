namespace DistIL.Passes;

using DistIL.IR;

//SSA transform implementation based on the standard dominance frontier algorithm.
public class SsaTransform2 : Pass
{
    Method _method = null!;
    DominatorTree _domTree = null!;
    Dictionary<PhiInst, Variable> _phiDefs = new(); //phi -> variable

    public override void Transform(Method method)
    {
        _method = method;
        _domTree = new DominatorTree(_method);

        InsertPhis();
        RenameDefs();
        PrunePhis();

        _phiDefs.Clear();
    }

    private void InsertPhis()
    {
        var varDefs = new Dictionary<Variable, ArrayStack<BasicBlock>>(); //var -> blocks assigning to var

        //Find variable definitions
        foreach (var inst in _method.Instructions()) {
            if (inst is StoreVarInst store && !store.Dest.IsExposed) {
                var worklist = varDefs.GetOrAddRef(store.Dest) ??= new();
                //Add parent block to the worklist, avoiding dupes
                if (worklist.Count == 0 || worklist.Top != store.Block) {
                    worklist.Push(store.Block);
                }
            }
        }

        var phiAdded = new HashSet<BasicBlock>(); //blocks where a phi has been added
        var processed = new HashSet<BasicBlock>(); //blocks already visited in worklist
        var domFrontier = new DominanceFrontier(_domTree);

        //Insert phis
        foreach (var (variable, worklist) in varDefs) {
            //Avoid inserting phis for variables only assigned in a single block
            if (worklist.Count == 1 && variable is not Argument) continue;

            //Initialize processed set (we do this to avoid keeping a whole HashSet for each variable)
            foreach (var def in worklist) {
                processed.Add(def);
            }
            //Insert phis on the DF of each block in the worklist
            while (worklist.TryPop(out var block)) {
                foreach (var dom in domFrontier.Of(block)) {
                    if (phiAdded.Add(dom)) {
                        var phi = dom.AddPhi(variable.ResultType);
                        _phiDefs.Add(phi, variable);

                        if (processed.Add(dom)) {
                            worklist.Push(dom);
                        }
                    }
                }
            }
            phiAdded.Clear();
            processed.Clear();
        }
    }

    private void RenameDefs()
    {
        //TODO: Push once per block (would need another dictionary, may not be worth)
        var defStacks = new Dictionary<Variable, ArrayStack<Value>>();
        var defDeltas = new ArrayStack<(BasicBlock B, Variable V)>();

        //Initialize argument defs to themselves: `Def(#a) = #a`
        //After SSA, they are considered readonly and can be used as a operand in any instruction.
        foreach (var arg in _method.Args) {
            var stack = new ArrayStack<Value>(1);
            stack.Push(arg);
            defStacks[arg] = stack;
        }
        defDeltas.Push((null!, null!)); //dummy element so we don't need to check IsEmpty in RestoreDefs

        _domTree.Traverse(
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
                if (inst is StoreVarInst store && !store.Dest.IsExposed) {
                    PushDef(block, store.Dest, store.Value);
                    store.Remove();
                }
                //Replace load with latest def
                else if (inst is LoadVarInst load && !load.Source.IsExposed) {
                    var currDef = ReadDef(load.Source);
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

    private void PrunePhis()
    {
        //Algorithm from the SSABook
        var usefulPhis = new HashSet<PhiInst>();
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
            foreach (var use in phi.Uses) {
                if (use.Inst is not PhiInst) {
                    return true;
                }
            }
            return false;
        }
    }
}