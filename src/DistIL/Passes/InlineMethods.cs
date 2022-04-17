namespace DistIL.Passes;

using DistIL.IR;

public class InlineMethods : Pass
{
    public override void Transform(Method method)
    {
        var inlineableCalls = new List<CallInst>();

        foreach (var inst in method.Instructions()) {
            if (inst is CallInst call && CanInline(method, call)) {
                inlineableCalls.Add(call);
            }
        }

        foreach (var call in inlineableCalls) {
            Inline(method, call);
        }
    }

    private static bool CanInline(Method caller, CallInst callInst)
    {
        if (callInst.Method is Method callee) {
            //TODO: better inlining heuristics
            //FIXME: block inlining for methods with accesses to private members of different classes
            return caller != callee && callee.NumBlocks <= 8;
        }
        return false;
    }

    private static void Inline(Method caller, CallInst callInst)
    {
        var callee = (Method)callInst.Method;
        var cloner = new Cloner(caller);

        //Add argument mappings
        for (int i = 0; i < callee.NumArgs; i++) {
            //Create temp variables because this transform happens before SSA
            var tempVar = new Variable(callee.ArgTypes[i]);
            var store = new StoreVarInst(tempVar, callInst.GetArg(i));
            store.InsertBefore(callInst);
            cloner.AddMapping(callee.Args[i], tempVar);
        }
        var newBlocks = cloner.CloneBlocks(callee);

        if (newBlocks.Count > 1) {
            InlineManyBlocks(callInst, newBlocks);
        } else {
            //Optimization: avoid creating new blocks for callees with one block
            InlineOneBlock(callInst, newBlocks[0]);
            caller.RemoveBlock(newBlocks[0]);
        }
    }

    private static void InlineOneBlock(CallInst callInst, BasicBlock block)
    {
        var returnedVal = ((ReturnInst)block.Last).Value;
        block.Last.Remove();

        //Move block instructions into caller block after callInst
        var callerBlock = callInst.Block;

        block.First.Prev = callInst;
        block.Last.Next = callInst.Next;
        callInst.Next!.Prev = block.Last;
        callInst.Next = block.First;

        foreach (var inst in block) {
            inst.Block = callerBlock;
        }
        //Replace call value
        if (callInst.HasResult) {
            callInst.ReplaceWith(returnedVal!);
        } else {
            callInst.Remove();
        }
    }

    private static void InlineManyBlocks(CallInst callInst, List<BasicBlock> blocks)
    {
        var startBlock = callInst.Block;
        var endBlock = startBlock.Split(callInst);

        var returnedVals = new List<PhiArg>();

        //Connect blocks into caller and convert returns to jumps into endBlock
        foreach (var block in blocks) {
            if (block == blocks[0]) { //entry block
                startBlock.SetBranch(block);
            }
            if (block.Last is ReturnInst ret) {
                if (callInst.HasResult) {
                    returnedVals.Add((block, ret.Value!));
                }
                block.SetBranch(endBlock);
            }
        }
        //Replace uses of the call with the returned value, or a phi for each returning block
        if (callInst.HasResult) {
            //FIXME: check if it' ok to add phis before the SSA pass
            var value = returnedVals.Count >= 2
                ? endBlock.AddPhi(returnedVals)
                : returnedVals[0].Value;
            callInst.ReplaceWith(value);
        } else {
            callInst.Remove();
        }
    }
}