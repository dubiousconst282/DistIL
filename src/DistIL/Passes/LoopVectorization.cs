namespace DistIL.Passes;

using DistIL.Analysis;

public class LoopVectorization : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var loopAnalysis = ctx.GetAnalysis<LoopAnalysis>(preserve: true);

        foreach (var loop in loopAnalysis.GetInnermostLoops()) {
            if (LoopSnapshot.TryCapture(loop) is not { } snap) continue;

            //Consider reduction loops
            foreach (var phi in loop.Header.Phis()) {
                
            }
        }

        return MethodInvalidations.None;
    }
}