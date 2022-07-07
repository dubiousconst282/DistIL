namespace DistIL.Passes;

using DistIL.IR;
using DistIL.IR.Utils;

public class InlineMethods : MethodPass
{
    public override void Run(MethodTransformContext ctx)
    {
        var inlineableCalls = new List<CallInst>();
        var method = ctx.Method;

        foreach (var inst in method.Instructions()) {
            if (inst is CallInst call && CanInline(method.Definition, call)) {
                inlineableCalls.Add(call);
            }
        }

        foreach (var call in inlineableCalls) {
            Inline(method, call);
        }
    }

    private static bool CanInline(MethodDef caller, CallInst callInst)
    {
        if (callInst.Method is MethodDef callee) {
            //TODO: better inlining heuristics
            //FIXME: block inlining for methods with accesses to private members of different classes
            return caller != callee && callee.Body?.NumBlocks <= 8;
        }
        return false;
    }

    private static void Inline(MethodBody caller, CallInst callInst)
    {
        var callee = ((MethodDef)callInst.Method).Body!;
        var cloner = new Cloner(caller);

        //Add argument mappings.
        //SSA transform guarantees that arguments are readonly, not address exposed, and inlined/not used in LoadVarInst.
        for (int i = 0; i < callee.Args.Length; i++) {
            cloner.AddMapping(callee.Args[i], callInst.GetArg(i));
        }
        var newBlocks = cloner.CloneBlocks(callee);

        if (newBlocks.Count > 1) {
            InlineManyBlocks(callInst, newBlocks);
        } else {
            //Optimization: avoid creating new blocks for callees with one block
            InlineOneBlock(callInst, newBlocks[0]);
            newBlocks[0].Remove();
        }
    }

    private static void InlineOneBlock(CallInst callInst, BasicBlock block)
    {
        //Move code (if not a single return)
        if (block.Last.Prev != null) {
            block.MoveRange(callInst.Block, callInst, block.First, block.Last.Prev!);
        }
        //Replace call value
        if (block.Last is ReturnInst ret && ret.HasValue) {
            callInst.ReplaceWith(ret.Value!);
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
        if (returnedVals.Count >= 2) {
            var value = endBlock.AddPhi(new PhiInst(returnedVals.ToArray()));
            callInst.ReplaceWith(value);
        } else if (returnedVals.Count == 1) {
            var (exitBlock, retValue) = returnedVals[0];
            callInst.ReplaceWith(retValue);
        } else {
            callInst.Remove();
        }
    }
}