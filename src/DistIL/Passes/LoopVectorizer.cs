namespace DistIL.Passes;

using DistIL.Analysis;
using DistIL.Passes.Vectorization;

public class LoopVectorizer : IMethodPass
{
    readonly VectorTranslator _trans;

    public LoopVectorizer(ModuleResolver resolver)
    {
        _trans = new VectorTranslator(resolver);
    }

    static IMethodPass IMethodPass.Create<TSelf>(Compilation comp)
        => new LoopVectorizer(comp.Resolver);

    public MethodPassResult Run(MethodTransformContext ctx)
    {
        if (!IsMarked(ctx.Definition) && !IsMarked(ctx.Definition.DeclaringType)) {
            return MethodInvalidations.None;
        }
        var loopAnalysis = ctx.GetAnalysis<LoopAnalysis>(preserve: true);
        bool changed = false;

        foreach (var loop in loopAnalysis.GetShapedLoops(innermostOnly: true)) {
            if (loop is not CountingLoopInfo forLoop) continue;
            
            changed |= InnerLoopVectorizer.TryVectorize(forLoop, _trans, ctx.Logger);
        }

        return changed ? MethodInvalidations.Loops : MethodInvalidations.None;
    }

    private static bool IsMarked(ModuleEntity entity)
    {
        return entity
            .GetCustomAttribs().Find("DistIL.Attributes", "OptimizeAttribute")
            ?.GetProperty("TryVectorize")?.Value is true;
    }
}