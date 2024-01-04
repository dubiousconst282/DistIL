namespace DistIL.Passes;

using DistIL.Analysis;
using DistIL.Frontend;

public interface IMethodPass
{
    MethodPassResult Run(MethodTransformContext ctx);

    /// <summary> Creates the pass instance through the default constructor of <typeparamref name="TSelf"/>. </summary>
    /// <remarks> Derived classes that don't have a default constructor _must_ implement this method. </remarks>
    static virtual IMethodPass Create<TSelf>(Compilation comp) where TSelf : IMethodPass
        => Activator.CreateInstance<TSelf>();
}

public readonly struct MethodPassResult
{
    public MethodInvalidations Changes { get; init; }
    // public IMethodAnalysis[] PreservedAnalyses { get; init; }

    public void InvalidateAffectedAnalyses(MethodTransformContext ctx)
    {
        // TODO: proper abstractions (IMethodAnalysis.AffectedByInvalidations?)
        if (Changes.HasFlag(MethodInvalidations.ControlFlow)) {
            ctx.Invalidate<DominatorTree>();
            ctx.Invalidate<DominanceFrontier>();
            ctx.Invalidate<LoopAnalysis>();
            ctx.Invalidate<ProtectedRegionAnalysis>();
        }
        if (Changes.HasFlag(MethodInvalidations.DataFlow)) {
            ctx.Invalidate<VarLivenessAnalysis>();
            ctx.Invalidate<LivenessAnalysis>();
            ctx.Invalidate<InterferenceGraph>();
            ctx.Invalidate<ForestAnalysis>();
        }
    }

    public static implicit operator MethodPassResult(MethodInvalidations changes)
        => new() { Changes = changes };
}
[Flags]
public enum MethodInvalidations
{
    None                = 0,
    Everything          = ~0,

    DataFlow            = 1 << 0,
    ControlFlow         = (1 << 1) | DataFlow,
    Loops               = (1 << 2) | ControlFlow,  // ControlFlow changes may imply loop changes, this is mostly for granularity. 
    ExceptionRegions    = (1 << 3) | ControlFlow,  // Likewise, ControlFlow changes may imply EHReg changes.
}

public class MethodTransformContext : IMethodAnalysisManager
{
    public Compilation Compilation { get; }
    public MethodBody Method { get; }

    public MethodDef Definition => Method.Definition;
    public ModuleDef Module => Method.Definition.Module;

    public ICompilationLogger Logger => Compilation.Logger;
    
    readonly Dictionary<Type, IMethodAnalysis> _analyses = new();

    public MethodTransformContext(Compilation comp, MethodBody method)
    {
        Compilation = comp;
        Method = method;
    }

    public A GetAnalysis<A>(bool preserve = true) where A : IMethodAnalysis
    {
        ref var analysis = ref _analyses.GetOrAddRef(typeof(A));
        analysis ??= A.Create(this);
        return (A)analysis;
    }
    public void Invalidate<A>() where A : IMethodAnalysis
    {
        _analyses.Remove(typeof(A));
    }
    public void InvalidateAll()
    {
        _analyses.Clear();
    }

    /// <summary> Gets or attempts to import a method body for IPO purposes. </summary>
    public MethodBody? GetMethodBodyForIPO(MethodDef method)
    {
        if (method.Module != Compilation.Module && !Compilation.Settings.AllowCrossAssemblyIPO) {
            return null;
        }
        if (method.Body == null && method.ILBody != null) {
            try {
                Logger.Debug($"Importing method for IPO: {method}");

                // FIXME: make this less precarious somehow (Compilation.DefaultPassesForIPO?)
                method.Body = ILImporter.ParseCode(method);

                var ctx = new MethodTransformContext(Compilation, method.Body);
                new SsaPromotion().Run(ctx);

                if (method.Name == ".ctor") {
                    new InlineMethods().Run(ctx);
                }
            } catch (Exception ex) {
                Logger.Error($"Failed to import method for IPO: {method}", ex);
            }
        }
        return method.Body;
    }
}

[AttributeUsage(AttributeTargets.Class)]
public class PassOptionsAttribute(string className) : Attribute
{
    public string ClassName { get; } = className;
}