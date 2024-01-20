namespace DistIL.Passes;

using System.Reflection;
using MethodBody = IR.MethodBody;

using DistIL.IR.Utils;
using DistIL.Frontend;

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
        // TODO: support for recursive inlines (without heuristics the code size will certainly blow up)
        var worklist = new ArrayStack<CallInst>();
        int numChanges = 0;

        foreach (var inst in ctx.Method.Instructions()) {
            if (inst is CallInst call) {
                worklist.Push(call);
            }
        }

        while (worklist.TryPop(out var call)) {
            if (!CheckAndMakeInlineable(ctx, call)) continue;

            if (Inline(call, onNewCall: worklist.Push)) {
                numChanges++;
            }
        }

        return numChanges > 0 ? MethodInvalidations.Everything : 0;
    }

    private bool CheckAndMakeInlineable(MethodTransformContext ctx, CallInst call)
    {
        // Must be a non-recursive MethodDef
        var parent = ctx.Method.Definition;

        if (call.Method is not MethodDefOrSpec { Definition: var target } || target == parent) {
            return false;
        }

        // Known virtual target
        if (call.IsVirtual && target.Attribs.HasFlag(MethodAttributes.Virtual) && !target.Attribs.HasFlag(MethodAttributes.Final)) {
            if (TypeUtils.ResolveVirtualMethod(call.Method, call.Args[0]) is not MethodDefOrSpec actualTarget || actualTarget == parent) {
                return false;
            }
            call.Method = actualTarget;
            target = actualTarget.Definition;
        }

        // Not too long
        int maxInstrs = target.ImplAttribs.HasFlag(MethodImplAttributes.AggressiveInlining)
            ? _opts.MaxCandidateInstrsAggressive
            : _opts.MaxCandidateInstrs;

        if (IsCandidateLinqMethod(target)) {
            maxInstrs = 128;
        }

        if (target.ImplAttribs.HasFlag(MethodImplAttributes.NoInlining) || target.ILBody?.Instructions.Count > maxInstrs) {
            return false;
        }

        // Not abstract nor pinvoke decl
        if (target.Attribs.HasFlag(MethodAttributes.Abstract) || target.Attribs.HasFlag(MethodAttributes.PinvokeImpl)) {
            return false;
        }

        // TODO: support for static virtuals
        if (target.Attribs.HasFlag(MethodAttributes.Static | MethodAttributes.Virtual)) {
            return false;
        }

        // Not special
        // For the time being we won't bother inlining async methods because there's
        // not much benefit for doing so, and it might mess up stack traces.
        if (target.HasCustomAttrib("System.Runtime.CompilerServices", "IntrinsicAttribute") ||
            target.HasCustomAttrib("System.Runtime.CompilerServices", "AsyncStateMachine")
        ) {
            return false;
        }

        // If there's no body after devirt, there's no way we can inline this method.
        if (target.Body == null && !ImportBodyForInlining(ctx, target)) {
            return false;
        }

        return true;
    }

    public static bool ImportBodyForInlining(MethodTransformContext ctx, MethodDef method)
    {
        // var effects = comp.GetAnalysis<GlobalFunctionEffects>().GetEffects(method);
        // var blockingTraits = FunctionTraits.HasStackAllocs | FunctionTraits.DoesNotReturn | FunctionTraits.Recursive;
        // if ((effects.Traits & blockingTraits) != 0) return false;

        return method.ILBody != null && IsCandidateLinqMethod(method) && ctx.GetMethodBodyForIPO(method) != null;
    }

    private static bool IsCandidateLinqMethod(MethodDef method)
    {
        if (method.Module.AsmName.Name == "System.Linq") {
            return true;
        }
        if (method.Name is "GetEnumerator" or "MoveNext") {
            // Inline enumerator methods for all but builtin collections
            var declType = method.DeclaringType;
            if (declType.IsNested) declType = declType.DeclaringType;

            return declType.Namespace != "System.Collections.Generic";
        }
        return false;
    }

    /// <summary> Unconditionally inlines the given call instruction into its parent method, unless the target method body is unavailable. </summary>
    public static bool Inline(CallInst call, Action<CallInst>? onNewCall = null)
    {
        if (call.Method is not MethodDefOrSpec { Definition.Body: MethodBody } target) {
            return false;
        }
        var result = Inline(call, target, call.Args, onNewCall);

        if (result != null) {
            call.ReplaceWith(result);
        } else {
            call.Remove();
        }
        return true;
    }

    public static Value? Inline(Instruction call, MethodDefOrSpec target, ReadOnlySpan<Value> args, Action<CallInst>? onNewCall = null)
    {
        var callerBody = call.Block.Method;
        var targetBody = Ensure.NotNull(target.Definition.Body);
        Ensure.That(callerBody != targetBody, "Cannot inline method into itself");

        var genCtx = new GenericContext(target);
        var cloner = onNewCall == null ? new IRCloner(callerBody, genCtx) : new MonitoringIRCloner(callerBody, target.Definition, onNewCall, genCtx);

        // Add argument mappings
        for (int i = 0; i < targetBody.Args.Length; i++) {
            var arg = targetBody.Args[i];
            var val = StoreInst.Coerce(arg.ResultType, args[i], insertBefore: call);
            cloner.AddMapping(arg, val);
        }

        // Clone blocks
        var returningBlocks = new List<BasicBlock>();
        var lastBlock = call.Block;

        foreach (var block in targetBody) {
            var newBlock = callerBody.CreateBlock(insertAfter: lastBlock);
            cloner.AddMapping(block, newBlock);
            lastBlock = newBlock;

            if (block.Last is ReturnInst) {
                returningBlocks.Add(newBlock);
            }
        }

        cloner.Run(targetBody.EntryBlock);
        returningBlocks.RemoveAll(cloner.IsDead);

        // If the target only has a single block ending with return,
        // the entire code can be moved at once without creating new blocks.
        var result = targetBody.NumBlocks == 1 && returningBlocks.Count == 1
            ? InlineOneBlock(call, returningBlocks[0])
            : InlineManyBlocks(call, cloner.GetMapping(targetBody.EntryBlock), returningBlocks);

        if (result != null) {
            // After inlining, `call` will be at the start of the continuation block.
            return result;
        } else if (target.ReturnType != PrimType.Void) {
            // This could happen if we inline a method that ends with ThrowInst, but it still returns something.
            // Code after `call` should be unreachable now, but we need to replace its uses with undef to avoid making the IR invalid.
            return new Undef(call.ResultType);
        }
        return null;
    }

    private static Value? InlineOneBlock(Instruction call, BasicBlock block)
    {
        var ret = (ReturnInst)block.Last;

        // Move code (if not a single return)
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

        var results = new PhiArg[returningBlocks.Count];
        int resultIdx = 0;

        // Rewrite exit blocks to jump into endBlock
        foreach (var block in returningBlocks) {
            if (block.Last is ReturnInst { HasValue: true } ret) {
                var result = StoreInst.Coerce(call.ResultType, ret.Value, insertBefore: ret);
                results[resultIdx++] = (block, result);
            }
            block.SetBranch(endBlock);
        }

        // Return the generated value
        if (resultIdx >= 2) {
            return endBlock.InsertPhi(new PhiInst(call.ResultType, results));
        }
        return results.FirstOrDefault().Value;
    }

    sealed class MonitoringIRCloner(MethodBody method, MethodDef CalledMethod, Action<CallInst> onNewCall, GenericContext genCtx) : IRCloner(method, genCtx)
    {
        public override Value Clone(Instruction inst)
        {
            var cloned = base.Clone(inst);

            // Ignore calls to recursive methods to prevent infinite loop.
            if (cloned is CallInst { Method: MethodDefOrSpec method } call && method.Definition != CalledMethod) {
                onNewCall.Invoke(call);
            }
            return cloned;
        }
    }

    [PassOptions("inliner")]
    public class Options
    {
        /// <summary> Inline candidate instruction limit. </summary>
        public int MaxCandidateInstrs { get; init; } = 32;

        /// <summary> Inline candidate instruction limit, for AggressiveInlining. </summary>
        public int MaxCandidateInstrsAggressive { get; init; } = 4096;
    }
}