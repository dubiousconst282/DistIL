namespace DistIL.Analysis;

using DistIL.IR;

public class LoopAnalysis : IMethodAnalysis
{
    public LoopAnalysis(DominatorTree domTree)
    {
    }

    public static IMethodAnalysis Create(IMethodAnalysisManager mgr)
    {
        return new LoopAnalysis(mgr.GetAnalysis<DominatorTree>(preserve: true));
    }
}
public class LoopInfo
{
    public BasicBlock Header { get; init; } = null!;
    public ValueSet<BasicBlock> Body { get; init; } = null!;

    public BasicBlock? PreHeader { get; init; }
}