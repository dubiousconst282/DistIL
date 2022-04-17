namespace DistIL.Passes;

using DistIL.IR;

//Alternative SSA transform that uses the standard dominance frontier algorithm.
public class SsaTransform2 : Pass
{
    Method _method = null!;
    DominatorTree _domTree = null!;
    Dictionary<Variable, List<BasicBlock>> _defBlocks = new(); //var -> blocks assigning to var
    Dictionary<PhiInst, Variable> _phiDefs = new(); //phi -> variable

    public override void Transform(Method method)
    {
        _method = method;
        _domTree = new DominatorTree(_method);

        FindDefs();
        InsertPhis();
        RenameDefs();

        PrunePhis();

        _defBlocks.Clear();
        _phiDefs.Clear();
    }

    private void FindDefs()
    {
        foreach (var inst in _method.Instructions()) {
            if (inst is StoreVarInst store) {
                var list = _defBlocks.GetOrAddRef(store.Dest) ??= new();
                list.Add(store.Block);
            }
        }
    }

    private void InsertPhis()
    {
        //TODO: avoid inserting useless phis
        var phiAdded = new HashSet<BasicBlock>();
        var processed = new HashSet<BasicBlock>();
        var pending = new Stack<BasicBlock>();

        var domFrontier = new DominanceFrontier(_domTree);

        foreach (var (variable, defs) in _defBlocks) {
            //Copy defs to pending stack (we do this to avoid keeping a whole HashSet for each variable)
            foreach (var def in defs) {
                if (processed.Add(def)) {
                    pending.Push(def);
                }
            }
            //Insert phis on the DF of each block in the pending stack
            while (pending.TryPop(out var block)) {
                foreach (var dom in domFrontier.Of(block)) {
                    if (phiAdded.Add(dom)) {
                        var phi = dom.AddPhi(variable.ResultType);
                        _phiDefs.Add(phi, variable);

                        if (processed.Add(dom)) {
                            pending.Push(dom);
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
            //Update defs for phis (they aren't assigned to real variables)
            foreach (var phi in block.Phis()) {
                if (_phiDefs.TryGetValue(phi, out var variable)) {
                    PushDef(block, variable, phi);
                }
            }
            foreach (var inst in block.NonPhis()) {
                //Update def
                if (inst is StoreVarInst store) {
                    PushDef(block, store.Dest, store.Value);
                    store.Remove();
                }
                //Replace loads with most recent defs
                else if (inst is LoadVarInst load) {
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
        //Initial marking phase
        foreach (var phi in _phiDefs.Keys) {
            if (IsTrivialPhi(phi)) {
                phi.ReplaceWith(phi.GetValue(0), false);
                continue;
            }
            foreach (var use in phi.Uses) {
                if (use.Inst is not PhiInst && usefulPhis.Add(phi)) {
                    propagationStack.Push(phi);
                    break;
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
        //Pruning phase
        foreach (var phi in _phiDefs.Keys) {
            if (!usefulPhis.Contains(phi)) {
                phi.Remove();
            }
        }
    }

    private bool IsTrivialPhi(PhiInst phi)
    {
        var value = phi.GetValue(0);
        for (int i = 1; i < phi.NumArgs; i++) {
            if (phi.GetValue(i) != value) {
                return false;
            }
        }
        return true;
    }
}