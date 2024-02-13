namespace DistIL.Analysis;

using DistIL.Frontend;
using DistIL.Passes;

using MethodAttributes = System.Reflection.MethodAttributes;
using ParameterAttributes = System.Reflection.ParameterAttributes;
using MethodImplAttributes = System.Reflection.MethodImplAttributes;

// InlinerAnalysis? InlineabilityAnalysis? I guess I'll just yoink from LLVM again.
public class InliningAdvisor : IGlobalAnalysis
{
    readonly Dictionary<MethodDef, (InlineRejectReason Reason, FunctionMetrics? Metrics)> _cache = new();
    readonly Compilation _comp;

    private InliningAdvisor(Compilation comp) => _comp = comp;

    static IGlobalAnalysis IGlobalAnalysis.Create(Compilation comp)
        => new InliningAdvisor(comp);

    public int EvaluateInliningCost(MethodBody targetBody, ReadOnlySpan<Value> args)
    {
        var (rejection, metrics) = GetMetrics(targetBody);

        if (rejection != InlineRejectReason.Accepted) {
            return int.MaxValue;
        }

        int cost = metrics!.BaseCost;

        for (int i = 0; i < targetBody.Args.Length; i++) {
            var argMetrics = metrics.Args[i];
            cost -= argMetrics.BenefitIfInlined;

            if (args[i] is Const) {
                cost -= argMetrics.BenefitIfKnownConst;
            } else if (TypeUtils.HasConcreteType(args[i])) {
                cost -= argMetrics.BenefitIfKnownType;
            }
        }

        int factor = GetAggressivenessFactor(targetBody.Definition);
        if (factor > 0) {
            cost /= factor;
        }

        return cost;
    }

    /// <summary> Gets or attempts to import a method body for IPO purposes. </summary>
    public MethodBody? GetMethodBodyForIPO(MethodDef method)
    {
        if (method.Module != _comp.Module && !_comp.Settings.AllowCrossAssemblyIPO) {
            return null;
        }
        if (method.Body == null && method.ILBody != null) {
            try {
                _comp.Logger.Debug($"Importing method for IPO: {method}");

                // FIXME: make this less precarious somehow (Compilation.DefaultPassesForIPO?)
                method.Body = ILImporter.ParseCode(method);

                var ctx = new MethodTransformContext(_comp, method.Body);
                new SsaPromotion().Run(ctx);

                if (method.Name == ".ctor") {
                    new InlineMethods().Run(ctx);
                }
            } catch (Exception ex) {
                _comp.Logger.Error($"Failed to import method for IPO: {method}", ex);
            }
        }
        return method.Body;
    }

    public bool ImportBodyForInlining(MethodDef method)
    {
        if (GetAggressivenessFactor(method) <= 2) {
            return false;
        }
        return GetMethodBodyForIPO(method) != null;
    }
    private static int GetAggressivenessFactor(MethodDef method)
    {
        // if (method.Module.AsmName.Name == "System.Linq") {
        //     return 3;
        // }
        // Avoid inlining calls to BCL / System.* methods to avoid bloating IL.
        if (method.Module.AsmName.Name!.StartsWith("System")) {
            return 1;
        }
        if (method.ImplAttribs.HasFlag(MethodImplAttributes.AggressiveInlining)) {
            return 5;
        }
        if (method.Name is "GetEnumerator" or "MoveNext") {
            // Inline enumerator methods for all but builtin collections
            var declType = method.DeclaringType;
            if (declType.IsNested) declType = declType.DeclaringType;

            return declType.Namespace != "System.Collections.Generic" ? 0 : 2;
        }
        return 0;
    }

    public (InlineRejectReason Rejection, FunctionMetrics? Metrics) GetMetrics(MethodBody method)
    {
        if (!_cache.TryGetValue(method.Definition, out var entry)) {
            entry = ComputeMetrics(method);
            _cache.Add(method.Definition, entry);

            if (entry.Reason != InlineRejectReason.Accepted) {
                _comp.Logger.Trace($"Rejecting inlinee '{method}': {entry.Reason}");
            }
        }
        return entry;
    }

    public void InvalidateMetrics(MethodBody method)
    {
        _cache.Remove(method.Definition);
    }

    public InlineRejectReason EarlyCheck(MethodDef method)
    {
        int maxInstrs = method.ImplAttribs.HasFlag(MethodImplAttributes.AggressiveInlining)
            ? 4096
            : 256;

        if (method.ILBody != null && method.ILBody.Instructions.Count > maxInstrs) {
            return InlineRejectReason.Overbudget;
        }

        if (method.ImplAttribs.HasFlag(MethodImplAttributes.NoInlining)) {
            return InlineRejectReason.Ineligible;
        }

        if (method.Attribs.HasFlag(MethodAttributes.Abstract) || 
            method.Attribs.HasFlag(MethodAttributes.PinvokeImpl) ||
            method.HasCustomAttrib("System.Runtime.CompilerServices", "IntrinsicAttribute")
        ) {
            return InlineRejectReason.UnknownTarget;
        }

        // Don't bother inlining async methods because we don't gain anything
        // from doing so yet, and it might mess up stack traces.
        if (method.HasCustomAttrib("System.Runtime.CompilerServices", "AsyncStateMachine")) {
            return InlineRejectReason.Ineligible;
        }

        return InlineRejectReason.Accepted;
    }

    private (InlineRejectReason Reason, FunctionMetrics? Metrics) ComputeMetrics(MethodBody method)
    {
        // Early bail on methods with too many blocks
        if (method.NumBlocks >= 200) {
            return (InlineRejectReason.Overbudget, null);
        }

        var metrics = new FunctionMetrics {
            Args = new ArgumentMetrics[method.Args.Length]
        };
        int i = 0;
        int numReturns = 0;

        foreach (var arg in method.Args) {
            ref var argMetrics = ref metrics.Args[i++];

            foreach (var user in arg.Users()) {
                UpdateArgumentUseInfo(ref argMetrics, user);
            }

            // Larger structs are more expansive to pass by value
            if (arg.ResultType is TypeDefOrSpec { IsValueType: true } type) {
                argMetrics.BenefitIfInlined += Math.Min(type.Definition.Fields.Count * 5, 50);
            }
            // Inlining functions with `out var` may enable promotion of exposed local vars
            else if (arg.Param.Attribs.HasFlag(ParameterAttributes.Out)) {
                argMetrics.BenefitIfInlined += 25;
            }
            // First few arguments are passed in registers, rest on stack
            else {
                argMetrics.BenefitIfInlined += (i > 4 ? 5 : 1);
            }
        }

        foreach (var block in method) {
            // Penalize exception regions because they're complicated
            foreach (var guard in block.Guards()) {
                metrics.BaseCost += guard.Kind == GuardKind.Finally ? 30 : 150;
            }

            foreach (var inst in block.NonPhis()) {
                // Cannot inline method with stackallocs
                if (inst is CilIntrinsic.Alloca) {
                    return (InlineRejectReason.HasStackAllocs, metrics);
                }
                if (inst is CallInst { Method: MethodDefOrSpec target }) {
                    // Cannot inline self-recursing method
                    if (target.Definition == method.Definition) {
                        return (InlineRejectReason.SelfRecursion, metrics);
                    }
                    metrics.BaseCost += Math.Min(target.ParamSig.Count * 2, 20);
                }
                metrics.BaseCost += 3;
            }

            // Penalize methods with more than one block
            if (block != method.EntryBlock) {
                metrics.BaseCost += 15;
            }

            // Throws suggest cold blocks.
            // This is probably a bad heuristic for methods that are too eager to validate arguments...
            // Would be better to penalize non-returning methods instead. 
            if (block.Last is ThrowInst) {
                metrics.BaseCost += 20;
            } else if (block.Last is ReturnInst) {
                numReturns++;
                metrics.BaseCost -= 3;
            }

            if (metrics.BaseCost >= 3000) { // bail if too long / expansive
                return (InlineRejectReason.Overbudget, metrics);
            }
        }

        if (numReturns == 0) {
            return (InlineRejectReason.DoesNotReturn, metrics);
        }
        return (InlineRejectReason.Accepted, metrics);
    }

    private static void UpdateArgumentUseInfo(ref ArgumentMetrics metrics, Instruction inst, int chainLen = 0)
    {
        bool checkDeps = false;

        // Devirt
        if (inst is CallInst { IsVirtual: true, Method: var callTarget }) {
            metrics.BenefitIfKnownType += callTarget.Attribs.HasFlag(MethodAttributes.Virtual) ? 130 : 80;
        }
        // Cast folding
        if (inst is CilIntrinsic.CastClass or CilIntrinsic.AsInstance) {
            metrics.BenefitIfKnownType += 30;
            checkDeps = true;
        }

        // Op folding
        if (inst is BinaryInst or CompareInst or UnaryInst or ConvertInst or SelectInst) {
            metrics.BenefitIfKnownConst += 10;
            checkDeps = true;
        }

        // Branch folding
        if (inst is BranchInst or SwitchInst) {
            // TODO: consider computing total number of succ blocks
            // (might be able to guesstimate by `PostOrderNo - PreOrderNo`?)
            metrics.BenefitIfKnownConst += 60;
        }

        // Traverse dependency chain
        if (checkDeps && chainLen <= 8) {
            foreach (var user in inst.Users()) {
                UpdateArgumentUseInfo(ref metrics, user, chainLen + 1);
            }
        }
    }

    public class FunctionMetrics
    {
        public ArgumentMetrics[] Args = [];
        public int BaseCost;
    }
    public struct ArgumentMetrics
    {
        public int BenefitIfInlined;      // some arguments may be more expansive to pass than others, e.g. large structs.
        public int BenefitIfKnownConst;
        public int BenefitIfKnownType;
    }
}

public enum InlineRejectReason
{
    Accepted,
    Ineligible,     // some other arbitrary reason
    UnknownTarget,  // method target is not known statically (e.g. abstract / pinvoke)
    Overbudget,
    SelfRecursion,
    HasStackAllocs,
    DoesNotReturn
}