namespace DistIL;

using DistIL.CodeGen.Cil;
using DistIL.Frontend;
using DistIL.Passes;

public class PassManager
{
    public required Compilation Compilation { get; init; }
    public List<PipelineSegment> Pipeline { get; } = new();
    public List<IPassInspector> Inspectors { get; } = new();

    /// <summary> Adds a new segment to the pass pipeline. </summary>
    /// <param name="applyIndependently"> Whether to apply the segment on all methods independently from others. </param>
    public PipelineSegment AddPasses(bool applyIndependently = false)
    {
        var pipe = new PipelineSegment(this, applyIndependently);
        Pipeline.Add(pipe);
        return pipe;
    }

    /// <summary> Apply passes to the given methods, while additionally importing and generating IL. </summary>
    public void Run(IReadOnlyCollection<MethodDef> methods)
    {
        foreach (var insp in Inspectors) {
            insp.OnBegin(Compilation);
        }

        ImportIL(methods);
        ApplyPasses(methods);
        GenerateIL(methods);

        foreach (var insp in Inspectors) {
            insp.OnFinish(Compilation);
        }
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

        var timer = Inspectors.OfType<PassTimingInspector>().FirstOrDefault();
        timer?.IncrementStat(typeof(ILImporter), startTs, count);
    }

    private void GenerateIL(IReadOnlyCollection<MethodDef> candidates)
    {
        long startTs = Stopwatch.GetTimestamp();
        int count = 0;

        foreach (var method in candidates) {
            try {
                if (method.Body != null) {
                    var spBuilder = method.GetDebugSymbols() != null ? new SequencePointBuilder(method) : null;
                    method.ILBody = ILGenerator.GenerateCode(method.Body, spBuilder);
                    spBuilder?.BuildAndReplace();
                    
                    count++;
                }
            } catch (Exception ex) {
                Compilation.Logger.Error($"Failed to generate code for '{method}'", ex);
            }
        }

        var timer = Inspectors.OfType<PassTimingInspector>().FirstOrDefault();
        timer?.IncrementStat(typeof(ILGenerator), startTs, count);
    }

    public void ApplyPasses(IReadOnlyCollection<MethodDef> candidates)
    {
        for (int i = 0; i < Pipeline.Count;) {
            int j = i + 1;

            // Group all serial segments
            if (!Pipeline[i]._applyIndependently) {
                while (j < Pipeline.Count && !Pipeline[j]._applyIndependently) {
                    j++;
                }
            }
            var segments = Pipeline.AsSpan()[i..j];

            foreach (var method in candidates) {
                if (method.Body == null) continue; // body will be null on failure to import the IL
                
                using var methodScope = Compilation.Logger.Push(s_ApplyScope, $"Apply passes to '{method}'");
                var ctx = new MethodTransformContext(Compilation, method.Body);

                try {
                    foreach (var seg in segments) {
                        seg.Run(ctx, Inspectors.AsSpan());
                    }
                } catch (Exception ex) {
                    method.Body = null;
                    Compilation.Logger.Error("Error while transforming method! Will fallback to original method body.", ex);
                }
            }
            i = j;
        }
    }

    static readonly LoggerScopeInfo s_ApplyScope = new("DistIL.PassManager.Apply");

    /// <summary>
    /// Searches for candidate transform methods and their dependencies through a depth-first IL scan. <br/>
    /// The returned list is ordered such that called methods appear before callers, if statically known.
    /// </summary>
    public static List<MethodDef> GetCandidateMethodsFromIL(ModuleDef module, CandidateMethodFilter? filter = null)
    {
        var candidates = new List<MethodDef>();

        var worklist = new ArrayStack<(MethodDef Method, bool Entered)>();
        var visited = new RefSet<MethodDef>();

        // Perform DFS on each defined method
        foreach (var seedMethod in module.MethodDefs()) {
            Push(null, seedMethod);

            while (!worklist.IsEmpty) {
                var (method, entered) = worklist.Top;

                if (!entered) {
                    worklist.Top.Entered = true;

                    // Enqueue called methods
                    foreach (ref var inst in method.ILBody!.Instructions.AsSpan()) {
                        if (inst.Operand is MethodDefOrSpec { Definition: var target }) {
                            Push(method, target);
                        }
                    }
                } else {
                    candidates.Add(method);
                    worklist.Pop();
                }
            }
        }
        return candidates;

        void Push(MethodDef? caller, MethodDef target)
        {
            if (target.ILBody != null && visited.Add(target)) {
                // Invoke the filter late as it may be expansive.
                if (filter != null && !filter.Invoke(caller, target)) return;

                worklist.Push((target, false));
            }
        }
    }

    public delegate bool CandidateMethodFilter(MethodDef? caller, MethodDef method);

    public class PipelineSegment
    {
        readonly PassManager _manager = null!;
        internal readonly bool _applyIndependently;

        readonly List<IMethodPass> _passes = new();
        PipelineSegment? _contIfChanged;
        int _maxIters = 1;

        internal PipelineSegment(PassManager manager, bool applyIndependently)
            => (_manager, _applyIndependently) = (manager, applyIndependently);

        public void Run(MethodTransformContext ctx, ReadOnlySpan<IPassInspector> inspectors)
        {
            for (int i = 0; i < _maxIters; i++) {
                if (!RunOnce(ctx, inspectors)) break;

                _contIfChanged?.Run(ctx, inspectors);
            }
        }

        private bool RunOnce(MethodTransformContext ctx, ReadOnlySpan<IPassInspector> inspectors)
        {
            bool changed = false;

            foreach (var pass in _passes) {
                foreach (var insp in inspectors) {
                    insp.OnBeforePass(pass, ctx);
                }

                var result = pass.Run(ctx);

                if (result.Changes != 0) {
                    result.InvalidateAffectedAnalyses(ctx);
                    changed = true;

                    ctx.Logger.Trace($"{pass.GetType().Name} changed {result.Changes}");
                }

                foreach (var insp in inspectors) {
                    insp.OnAfterPass(pass, ctx, result);
                }
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