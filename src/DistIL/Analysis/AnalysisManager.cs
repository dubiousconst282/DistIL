namespace DistIL.Analysis;

public interface IAnalysis
{
}
public interface IGlobalAnalysis : IAnalysis
{
    static virtual IGlobalAnalysis Create(Compilation comp) => throw new NotImplementedException();
}
public interface IMethodAnalysis : IAnalysis
{
    static virtual IMethodAnalysis Create(IMethodAnalysisManager mgr) => throw new NotImplementedException();
}
public interface IMethodAnalysisManager
{
    MethodBody Method { get; }

    A GetAnalysis<A>(bool preserve = true) where A : IMethodAnalysis;
    
    /// <summary> Invalidates all currently cached analyses. </summary>
    void InvalidateAll();
}