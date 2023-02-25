namespace DistIL.Passes;

using DistIL.IR.Utils;

using ImplAttribs = System.Reflection.MethodImplAttributes;
using MethodAttribs = System.Reflection.MethodAttributes;

public class InlineMethods : IMethodPass
{
    readonly Options _opts;

    public InlineMethods(Options? opts = null)
    {
        _opts = opts ?? new();
    }

    static IMethodPass IMethodPass.Create<TSelf>(Compilation comp)
        => new InlineMethods();

    public MethodPassResult Run(MethodTransformContext ctx)
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

        return inlineableCalls.Count > 0 ? MethodInvalidations.Everything : 0;
    }

    private bool CanBeInlined(MethodDef caller, CallInst callInst)
    {
        //TODO: proper checks - II.10.3 Introducing and overriding virtual methods
        var blockedAttribs = MethodAttribs.NewSlot | MethodAttribs.Abstract | MethodAttribs.PinvokeImpl;
        var staticVirt = MethodAttribs.Static | MethodAttribs.Virtual;

        return callInst.Method is MethodDefOrSpec { Definition: var callee } &&
            callee != caller &&
            callee.ILBody?.Instructions.Count <= _opts.MaxCalleeSize &&
            (callee.Attribs & blockedAttribs) == 0 &&
            (callee.Attribs & staticVirt) != staticVirt &&
            (callee.ImplAttribs & ImplAttribs.NoInlining) == 0 &&
            !callee.HasCustomAttrib("System.Runtime.CompilerServices", "IntrinsicAttribute") &&
            !callee.HasCustomAttrib("System.Runtime.CompilerServices", "AsyncStateMachine");
    }

    public static bool Inline(CallInst call)
    {
        if (call.Method is not MethodDefOrSpec { Definition.Body: MethodBody } callee) {
            return false;
        }
        var result = Inline(call, callee, call.Args);

        if (result != null) {
            call.ReplaceWith(result);
        } else {
            call.Remove();
        }
        return true;
    }

    public static Value? Inline(Instruction call, MethodDefOrSpec callee, ReadOnlySpan<Value> args)
    {
        var callerBody = call.Block.Method;
        var calleeBody = Ensure.NotNull(callee.Definition.Body);
        var cloner = new IRCloner(new GenericContext(callee));

        //Add argument mappings
        for (int i = 0; i < calleeBody.Args.Length; i++) {
            var arg = calleeBody.Args[i];
            var val = StoreInst.Coerce(arg.ResultType, args[i], insertBefore: call);
            cloner.AddMapping(arg, val);
        }

        //Clone blocks
        var returningBlocks = new List<BasicBlock>();
        var lastBlock = call.Block;

        foreach (var block in calleeBody) {
            var newBlock = callerBody.CreateBlock(insertAfter: lastBlock);
            cloner.AddBlock(block, newBlock);
            lastBlock = newBlock;

            if (block.Last is ReturnInst) {
                returningBlocks.Add(newBlock);
            }
        }
        cloner.Run();

        //If the callee only has a single block ending with return,
        //the entire code can be moved at once without creating new blocks.
        var result = calleeBody.NumBlocks == 1 && returningBlocks.Count == 1
            ? InlineOneBlock(call, returningBlocks[0])
            : InlineManyBlocks(call, cloner.GetMapping(calleeBody.EntryBlock), returningBlocks);

        if (result != null) {
            //After inlining, `call` will be at the start of the continuation block.
            return result;
        } else if (callee.ReturnType != PrimType.Void) {
            //This could happen if we inline a method that ends with ThrowInst, but it still returns something.
            //Code after `call` should be unreachable now, but we need to replace its uses with undef to avoid making the IR invalid.
            return new Undef(call.ResultType);
        }
        return null;
    }

    private static Value? InlineOneBlock(Instruction call, BasicBlock block)
    {
        var ret = (ReturnInst)block.Last;

        //Move code (if not a single return)
        if (ret.Prev != null) {
            block.MoveRange(call.Block, call.Prev, block.First, ret.Prev);
        }
        block.Remove();
        
        if (ret.Value != null) {
            return StoreInst.Coerce(call.ResultType, ret.Value, insertBefore: call);
        }
        return null;
    }

    private static Value? InlineManyBlocks(Instruction call, BasicBlock entryBlock, List<BasicBlock> returningBlocks)
    {
        var startBlock = call.Block;
        var endBlock = startBlock.Split(call, branchTo: entryBlock);

        var resultType = call.ResultType;
        var results = new PhiArg[returningBlocks.Count];
        int resultIdx = 0;

        //Rewrite exit blocks to jump into endBlock
        foreach (var block in returningBlocks) {
            if (block.Last is ReturnInst { HasValue: true } ret) {
                var result = StoreInst.Coerce(resultType, ret.Value, insertBefore: ret);
                results[resultIdx++] = (block, result);
            }
            block.SetBranch(endBlock);
        }

        //Return the generated value
        if (resultIdx >= 2) {
            return endBlock.InsertPhi(new PhiInst(resultType, results));
        }
        return results.FirstOrDefault().Value;
    }

    public class Options
    {
        /// <summary> Ignore callees whose number of IL instructions is greater than this. </summary>
        public int MaxCalleeSize { get; init; } = 32;

        // <summary> If true, allows calls to methods from different assemblies to be inlined. </summary>
        //public bool InlineCrossAssemblyCalls { get; init; } = false;

        // <summary> If true, private members accessed by callee will be exposed as public (if they are on the same assembly). </summary>
        //public bool ExposePrivateCalleeMembers { get; init; } = true;

        //Note that IACA is undocumented: https://github.com/dotnet/runtime/issues/37875
        //public bool UseIgnoreAccessChecksAttribute { get; init; } = false;
    }
}