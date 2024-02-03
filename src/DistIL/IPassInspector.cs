namespace DistIL;

using DistIL.Passes;

public interface IPassInspector
{
    void OnBeforePass(IMethodPass pass, MethodTransformContext ctx) { }
    void OnAfterPass(IMethodPass pass, MethodTransformContext ctx, MethodPassResult result) { }

    void OnBegin(Compilation comp) { }
    void OnFinish(Compilation comp) { }
}

public class PassTimingInspector(bool logStatsOnFinish = true) : IPassInspector
{
    public Dictionary<Type, (int NumChanges, TimeSpan TimeTaken)> Stats { get; } = new();

    long _startTs;
    IMethodPass? _activePass;

    void IPassInspector.OnBeforePass(IMethodPass pass, MethodTransformContext ctx)
    {
        _activePass = pass;
        _startTs = Stopwatch.GetTimestamp();
    }
    void IPassInspector.OnAfterPass(IMethodPass pass, MethodTransformContext ctx, MethodPassResult result)
    {
        if (_activePass == null) {
            throw new InvalidOperationException();
        }
        IncrementStat(pass.GetType(), _startTs, result.Changes != MethodInvalidations.None ? 1 : 0);
        _activePass = null;
    }

    void IPassInspector.OnFinish(Compilation comp)
    {
        if (logStatsOnFinish) {
            LogStats(comp.Logger);
        }
    }

    public void IncrementStat(Type passType, long startTs, int numChanges)
    {
        ref var stat = ref Stats.GetOrAddRef(passType);
        stat.TimeTaken += Stopwatch.GetElapsedTime(startTs);
        stat.NumChanges += numChanges;
    }

    public void LogStats(ICompilationLogger logger)
    {
        using var scope = logger.Push(new("DistIL.PassManager.ResultStats"), "Pass statistics");
        var totalTime = TimeSpan.Zero;

        foreach (var (key, stats) in Stats) {
            logger.Info($"{key.Name}: made {stats.NumChanges} changes in {stats.TimeTaken.TotalSeconds:0.00}s");
            totalTime += stats.TimeTaken;
        }
        logger.Info($"-- Total time: {totalTime.TotalSeconds:0.00}s");
    }
}