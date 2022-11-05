namespace DistIL.Passes;

using DistIL.IR.Utils;

using MethodAttrs = System.Reflection.MethodAttributes;

public class InlineMethods : MethodPass
{
    readonly Options _opts;

    public InlineMethods(Options? opts = null)
    {
        _opts = (opts ?? new());
    }

    public override void Run(MethodTransformContext ctx)
    {
        var inlineableCalls = new List<CallInst>();
        var method = ctx.Method;

        foreach (var inst in method.Instructions()) {
            if (inst is CallInst call && CanBeInlined(method.Definition, call)) {
                inlineableCalls.Add(call);
            }
        }

        foreach (var call in inlineableCalls) {
            Inline(call);
        }
    }

    private bool CanBeInlined(MethodDef caller, CallInst callInst)
    {
        if (callInst.Method is not MethodDefOrSpec callee || callee == caller) {
            return false;
        }
        if (callInst.IsVirtual && (callee.Attribs & MethodAttrs.NewSlot) != 0) {
            return false;
        }
        if (callee.Definition.ILBody?.Instructions.Count > _opts.MaxCalleeSize) {
            return false;
        }
        return true;
    }

    public static bool Inline(CallInst call)
    {
        if (call.Method is not MethodDefOrSpec { Definition.Body: MethodBody calleeBody } callee) {
            return false;
        }
        var callerBody = call.Block.Method;
        var cloner = new IRCloner();

        //Add argument mappings
        for (int i = 0; i < calleeBody.Args.Length; i++) {
            cloner.AddMapping(calleeBody.Args[i], call.GetArg(i));
        }
        //Add generic type mappings
        if (callee.IsGenericSpec) {
            var emptyParams = callee.Definition.GenericParams;
            var filledArgs = callee.GenericParams;

            for (int i = 0; i < filledArgs.Length; i++) {
                cloner.AddMapping(emptyParams[i], filledArgs[i]);
            }
        }
        //Clone blocks
        var newBlocks = new List<BasicBlock>();
        foreach (var block in calleeBody) {
            var newBlock = callerBody.CreateBlock(insertAfter: newBlocks.LastOrDefault(call.Block));
            cloner.AddBlock(block, newBlock);
            newBlocks.Add(newBlock);
        }
        cloner.Run();

        if (newBlocks is [{ Last: ReturnInst }]) {
            //Opt: avoid creating new blocks for callees with a single block ending with a return
            InlineOneBlock(call, newBlocks[0]);
            newBlocks[0].Remove();
        } else {
            InlineManyBlocks(call, newBlocks);
        }
        return true;
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
            call.ReplaceWith(returnedVals[0].Value);
        } else {
            call.Remove();
        }
    }

    public class Options
    {
        /// <summary> Ignore callees whose number of IL instructions is greater than this. </summary>
        public int MaxCalleeSize { get; init; } = 64;

        /// <summary> If true, allows calls to methods from different assemblies to be inlined. </summary>
        //public bool InlineCrossAssemblyCalls { get; init; } = false;

        /// <summary> If true, private member accessed by callee will be exposed as public (if they are on the same assembly). </summary>
        //public bool ExposePrivateCalleeMembers { get; init; } = true;

        //Note that IACA is undocumented: https://github.com/dotnet/runtime/issues/37875
        //public bool UseIgnoreAccessChecksAttribute { get; init; } = false;
    }
}