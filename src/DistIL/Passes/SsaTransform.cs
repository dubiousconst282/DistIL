namespace DistIL.Passes;

using DistIL.IR;

//Algorithm from the paper "Simple and Efficient Construction of Static Single Assignment Form"
//https://pp.ipd.kit.edu/uploads/publikationen/braun13cc.pdf
public class SsaTransform : MethodPass
{
    readonly Dictionary<(BasicBlock, Variable), Value> _currDefs = new();
    readonly HashSet<BasicBlock> _sealedBlocks = new();
    readonly List<(PhiInst, Variable)> _incompletePhis = new();

    public override void Transform(MethodBody method)
    {
        var entryBlock = method.EntryBlock;
        var blocks = new List<BasicBlock>();
        GraphTraversal.DepthFirst(
            entry: method.EntryBlock,
            getChildren: b => b.Succs,
            postVisit: blocks.Add
        );

        //Init the definition map for arguments (set values to themselves)
        foreach (var arg in method.Args) {
            WriteVar(entryBlock, arg, arg);
        }
        
        foreach (var block in blocks.ReverseItr()) {
            foreach (var inst in block) {
                if (inst is StoreVarInst store) {
                    WriteVar(inst.Block, store.Dest, store.Value);
                    inst.Remove();
                    continue;
                }
                if (inst is LoadVarInst load) {
                    var currVal = ReadVar(inst.Block, load.Source);
                    inst.ReplaceWith(currVal);
                    continue;
                }
                //we only handle variables read/written with LdVar or StVar
                Assert(inst.Operands.All(v => v is not Variable || v is Argument));
            }
            _sealedBlocks.Add(block);
        }
        RewriteIncompletePhis();

        _currDefs.Clear();
        _sealedBlocks.Clear();
        _incompletePhis.Clear();
    }

    private void WriteVar(BasicBlock block, Variable var, Value val)
    {
        while (val is Variable srcVar && val is not Argument) {
            val = ReadVar(block, srcVar); //copy propagation
        }
        _currDefs[(block, var)] = val;
    }
    //TODO: use iterative algorithm to avoid blowing up the call stack
    private Value ReadVar(BasicBlock block, Variable var)
    {
        if (_currDefs.TryGetValue((block, var), out var val)) {
            return val;
        }
        var preds = block.Preds;

        if (!_sealedBlocks.Contains(block)) {
            //Block not processed yet, add a proxy phi and handle it later
            var phi = block.AddPhi(var.ResultType);
            _incompletePhis.Add((phi, var));
            val = phi;
        } else if (preds.Count > 1) {
            var phi = block.AddPhi(var.ResultType);
            //Write dummy phi to avoid infinite recursion
            WriteVar(block, var, phi);
            val = AddPhiOperands(phi, var);
        } else if (preds.Count == 1) {
            //Recurse into the only one predecessor
            val = ReadVar(preds[0], var);
        } else {
            //Uninitialized variable on the entry block
            val = new Undef(var.ResultType);
        }
        WriteVar(block, var, val);
        return val;
    }

    private Value AddPhiOperands(PhiInst phi, Variable var, bool removeTrivial = true)
    {
        var preds = phi.Block.Preds;
        var args = new PhiArg[preds.Count];
        for (int i = 0; i < preds.Count; i++) {
            var pred = preds[i];
            var value = ReadVar(pred, var);
            args[i] = (pred, value);
        }
        phi.AddArg(args);

        return removeTrivial ? TryRemoveTrivialPhi(phi) : phi;
    }
    private Value TryRemoveTrivialPhi(PhiInst phi)
    {
        Value? same = null;
        for (int i = 0; i < phi.NumArgs; i++) {
            var op = phi.GetValue(i);
            if (op == same || op == phi) continue; //same value or self-reference
            if (same != null) return phi; //phi merges at least two values: not trivial
            same = op;
        }
        if (same == null) {
            //self referencing phis are unreachable or in the start block, replace it with undef
            same = new Undef(phi.ResultType);
        }
        var users = phi.Users();
        if (same != phi) {
            phi.ReplaceWith(same, insertIfInst: false);
        }
        foreach (var user in users) {
            if (user is PhiInst otherPhi && otherPhi != phi) {
                TryRemoveTrivialPhi(otherPhi);
            }
        }
        return same;
    }

    private void RewriteIncompletePhis()
    {
        //Removing trivial phis in a separate pass because later calls 
        //to AddPhiOperands() may add a use to a removed incomplete phi.
        //Updating the def with the new value won't work because uses may 
        //not be using the same var in the tuple.
        //This doesn't feel like the best way to do it, but it should do for now.
        foreach (var (phi, var) in _incompletePhis) {
            AddPhiOperands(phi, var, false);
        }
        foreach (var (phi, var) in _incompletePhis) {
            TryRemoveTrivialPhi(phi);
        }
    }
}