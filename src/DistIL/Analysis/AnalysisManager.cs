namespace DistIL.Analysis;

using DistIL.IR;

public interface IAnalysis
{
}
public interface IMethodAnalysis : IAnalysis
{
    static abstract IMethodAnalysis Create(IMethodAnalysisManager mgr);
}
public interface IMethodAnalysisManager
{
    MethodBody Method { get; }

    A GetAnalysis<A>(bool preserve = false) where A : IMethodAnalysis;
    void Preserve<A>() where A : IMethodAnalysis;
}