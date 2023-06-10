namespace DistIL;

using DistIL.CodeGen.Cil;
using DistIL.Frontend;
using DistIL.Passes;

public class PassManager
{
    public required Compilation Compilation { get; init; }
    public List<PipelineSegment> Pipeline { get; } = new();
    public bool TrackAndLogStats { get; init; }

    readonly Dictionary<Type, (int NumChanges, TimeSpan TimeTaken)> _stats = new();

    /// <summary> Adds a new segment to the pass pipeline. </summary>
    /// <param name="applyIndependently"> Whether to apply the segment on all methods independently from others. </param>
    public PipelineSegment AddPasses(bool applyIndependently = false)
    {
        var pipe = new PipelineSegment(this, applyIndependently);
        Pipeline.Add(pipe);
        return pipe;
    }

    public void Run(IReadOnlyCollection<MethodDef> methods)
    {
        ImportIL(methods);
        ApplyPasses(methods);
        GenerateIL(methods);

        LogStats();
    }

    private void ImportIL(IReadOnlyCollection<MethodDef> candidates)
    {
        long startTs = Stopwatch.GetTimestamp();
        int count = 0;

        foreach (var method in candidates) {
            try {
                if (method.ILBody != null && method.Body == null) {
                    method.Body = ILImporter.ParseCode(method);
                    count++;
                }
            } catch (Exception ex) {
                Compilation.Logger.Error($"Failed to import code from '{method}'", ex);
            }
        }
        IncrementStat(typeof(ILImporter), startTs, count);
    }

    private void GenerateIL(IReadOnlyCollection<MethodDef> candidates)
    {
        long startTs = Stopwatch.GetTimestamp();
        int count = 0;

        foreach (var method in candidates) {
            try {
                if (method.Body != null) {
                    method.ILBody = ILGenerator.GenerateCode(method.Body);
                    count++;
                }
            } catch (Exception ex) {
                Compilation.Logger.Error($"Failed to generate code for '{method}'", ex);
            }
        }
        IncrementStat(typeof(ILGenerator), startTs, count);
    }

    private void ApplyPasses(IReadOnlyCollection<MethodDef> candidates)
    {
        for (int i = 0; i < Pipeline.Count;) {
            int j = i + 1;

            //Group all serial segments
            if (!Pipeline[i]._applyIndependently) {
                while (j < Pipeline.Count && !Pipeline[j]._applyIndependently) {
                    j++;
                }
            }
            var segments = Pipeline.AsSpan()[i..j];

            foreach (var method in candidates) {
                using var methodScope = Compilation.Logger.Push(s_ApplyScope, $"Apply passes to '{method}'");
                var ctx = new MethodTransformContext(Compilation, method.Body!);

                foreach (var seg in segments) {
                    seg.Run(ctx);
                }
            }
            i = j;
        }
    }

    internal void IncrementStat(Type passType, long startTs, int numChanges)
    {
        if (!TrackAndLogStats) return;

        ref var stat = ref _stats.GetOrAddRef(passType);
        stat.TimeTaken += Stopwatch.GetElapsedTime(startTs);
        stat.NumChanges += numChanges;
    }

    private void LogStats()
    {
        if (!TrackAndLogStats) return;

        using var scope = Compilation.Logger.Push(s_StatsScope, "Pass statistics");
        var totalTime = TimeSpan.Zero;

        foreach (var (key, stats) in _stats) {
            Compilation.Logger.Info($"{key.Name}: made {stats.NumChanges} changes in {stats.TimeTaken.TotalSeconds:0.00}s");
            totalTime += stats.TimeTaken;
        }
        Compilation.Logger.Info($"-- Total time: {totalTime.TotalSeconds:0.00}s");
    }

    static readonly LoggerScopeInfo
        s_ApplyScope = new("DistIL.PassManager.Apply"),
        s_StatsScope = new("DistIL.PassManager.ResultStats");

    /// <summary>
    /// Searches for candidate transform methods and their dependencies via IL scan. <br/>
    /// The returned list is ordered such that called methods appear before callers, if statically known.
    /// </summary>
    public static List<MethodDef> GetCandidateMethodsFromIL(ModuleDef module, Predicate<MethodDef>? filter = null)
    {
        var candidates = new List<MethodDef>();

        var worklist = new ArrayStack<(MethodDef Method, bool Entered)>();
        var visited = new RefSet<MethodDef>();

        //Perform DFS on each defined method
        foreach (var seedMethod in module.MethodDefs()) {
            Push(seedMethod);

            while (!worklist.IsEmpty) {
                var (method, entered) = worklist.Top;

                if (!entered) {
                    worklist.Top.Entered = true;

                    //Enqueue called methods
                    foreach (ref var inst in method.ILBody!.Instructions.AsSpan()) {
                        if (inst.Operand is MethodDefOrSpec { Definition: var callee }) {
                            Push(callee);
                        }
                    }
                } else {
                    candidates.Add(method);
                    worklist.Pop();
                }
            }
        }
        return candidates;

        void Push(MethodDef method)
        {
            if (IsCandidate(method) && visited.Add(method)) {
                worklist.Push((method, false));
            }
        }
        bool IsCandidate(MethodDef method)
        {
            return method.ILBody != null &&
                   method.Module == module && 
                   (filter == null || filter.Invoke(method));
        }
    }

    public class PipelineSegment
    {
        readonly PassManager _manager = null!;
        internal readonly bool _applyIndependently;

        readonly List<IMethodPass> _passes = new();
        PipelineSegment? _contIfChanged;
        int _maxIters = 1;

        internal PipelineSegment(PassManager manager, bool applyIndependently)
            => (_manager, _applyIndependently) = (manager, applyIndependently);

        public void Run(MethodTransformContext ctx)
        {
            for (int i = 0; i < _maxIters; i++) {
                if (!RunOnce(ctx)) break;

                _contIfChanged?.Run(ctx);
            }
        }

        private bool RunOnce(MethodTransformContext ctx)
        {
            bool changed = false;

            foreach (var pass in _passes) {
                long startTs = Stopwatch.GetTimestamp();
                var result = pass.Run(ctx);

                if (result.Changes != 0) {
                    result.InvalidateAffectedAnalyses(ctx);
                    changed = true;

                    ctx.Logger.Trace($"{pass.GetType().Name} changed {result.Changes}");
                }
                _manager.IncrementStat(pass.GetType(), startTs, result.Changes != 0 ? 1 : 0);
            }
            return changed;
        }

        public PipelineSegment Apply<TPass>() where TPass : IMethodPass
        {
            return Apply(TPass.Create<TPass>(_manager.Compilation));
        }
        public PipelineSegment Apply(IMethodPass pass)
        {
            _passes.Add(pass);
            return this;
        }

        public PipelineSegment IfChanged(Action<PipelineSegment> buildChildPipe)
        {
            Ensure.That(_contIfChanged == null);

            _contIfChanged = new PipelineSegment(_manager, false);
            buildChildPipe(_contIfChanged);

            return this;
        }
        public PipelineSegment IfChanged(PipelineSegment segmentToApply)
        {
            Ensure.That(_contIfChanged == null);
            _contIfChanged = segmentToApply;
            return this;
        }
        public PipelineSegment RepeatUntilFixedPoint(int maxIters)
        {
            _maxIters = maxIters;

            return this;
        }
    }
}