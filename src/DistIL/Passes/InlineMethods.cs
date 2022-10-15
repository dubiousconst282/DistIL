namespace DistIL.Passes;

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
            Inline(call);
        }
    }

    private static bool CanInline(MethodDef caller, CallInst callInst)
    {
        if (callInst.Method is not MethodDef callee) {
            return false;
        }
        if (callInst.IsVirtual && (callee.Attribs & System.Reflection.MethodAttributes.NewSlot) != 0) {
            return false;
        }
        //TODO: better inlining heuristics
        //FIXME: block inlining for methods with accesses to private members of different classes
        return caller != callee && callee.Body?.NumBlocks <= 8;
    }

    public static void Inline(CallInst call)
    {   
        var callee = ((MethodDef)call.Method).Body!;
        var cloner = new Cloner(call.Block.Method);

        //Add argument mappings
        for (int i = 0; i < callee.Args.Length; i++) {
            cloner.AddMapping(callee.Args[i], call.GetArg(i));
        }
        //Clone blocks
        var newBlocks = new List<BasicBlock>();
        foreach (var block in callee) {
            var newBlock = cloner.AddBlock(block, insertAfter: newBlocks.LastOrDefault(call.Block));
            newBlocks.Add(newBlock);
        }
        cloner.Run();

        if (newBlocks is [{ Last: ReturnInst }]) {
            //opt: avoid creating new blocks for callees with a single block ending with a return
            InlineOneBlock(call, newBlocks[0]);
            newBlocks[0].Remove();
        } else {
            InlineManyBlocks(call, newBlocks);
        }
    }

    private static void InlineOneBlock(CallInst call, BasicBlock block)
    {
        //Move code (if not a single return)
        if (block.Last.Prev != null) {
            block.MoveRange(call.Block, call, block.First, block.Last.Prev);
        }
        //Replace call value
        if (block.Last is ReturnInst ret && ret.HasValue) {
            call.ReplaceWith(ret.Value);
        } else {
            call.Remove();
        }
    }

    private static void InlineManyBlocks(CallInst call, List<BasicBlock> blocks)
    {
        var startBlock = call.Block;
        var endBlock = startBlock.Split(call);

        var returnedVals = new List<PhiArg>();

        //Connect blocks into caller and convert returns to jumps into endBlock
        foreach (var block in blocks) {
            if (block == blocks[0]) { //entry block
                startBlock.SetBranch(block);
            }
            if (block.Last is ReturnInst ret) {
                if (ret.HasValue) {
                    returnedVals.Add((block, ret.Value));
                }
                block.SetBranch(endBlock);
            }
        }
        //Replace uses of the call with the returned value, or a phi for each returning block
        if (returnedVals.Count >= 2) {
            var value = endBlock.AddPhi(new PhiInst(returnedVals.ToArray()));
            call.ReplaceWith(value);
        } else if (returnedVals.Count == 1) {
            var (exitBlock, retValue) = returnedVals[0];
            call.ReplaceWith(retValue);
        } else {
            call.Remove();
        }
    }
}