namespace DistIL.Passes;

using DistIL.Passes.Linq;

public class ExpandLinq : IMethodPass
{
    readonly TypeDefOrSpec t_Enumerable, t_IEnumerableOfT0;

    public ExpandLinq(ModuleResolver resolver)
    {
        t_Enumerable = resolver.Import(typeof(Enumerable));
        t_IEnumerableOfT0 = resolver.Import(typeof(IEnumerable<>)).GetSpec(GenericContext.Empty);
    }

    static IMethodPass IMethodPass.Create<TSelf>(Compilation comp)
        => new ExpandLinq(comp.Resolver);

    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var queries = new List<LinqSourceNode>();

        foreach (var inst in ctx.Method.Instructions()) {
            if (inst is CallInst call && CreatePipe(call) is { } pipe) {
                queries.Add(pipe);
            }
        }

        foreach (var query in queries) {
            query.Emit();
        }
        return queries.Count > 0 ? MethodInvalidations.Loops : 0;
    }

    private LinqSourceNode? CreatePipe(CallInst call)
    {
        var sink = CreateSink(call);
        if (sink == null) {
            return null;
        }
        var source = CreateStage(call.GetOperandRef(0), sink);
        return IsProfitableToExpand(source, sink) ? source : null;
    }

    private static bool IsProfitableToExpand(LinqSourceNode source, LinqSink sink)
    {
        // Unfiltered Count()/Any() is not profitable because we always scan over
        // the entire source, and LINQ specializes over collections and such. 
        if (sink.SubjectCall is { NumArgs: 1, Method.Name: "Count" or "Any" }) {
            return false;
        }
        // Expanding enumerator sources may not be profitable because 
        // Linq can special-case source types and defer to e.g. Array.Copy().
        // Similarly, expanding an enumerator source to a loop sink is an expansive no-op.
        if (source is EnumeratorSource && source.Drain == sink) {
            return sink is not (ConcretizationSink or LoopSink);
        }
        // Range().ToArray() and ToList() are already special-cased by LINQ, and vectorized in .NET 8.
        // - https://github.com/dubiousconst282/DistIL/issues/25
        // - https://github.com/dotnet/runtime/pull/87992
        if (source is IntRangeSource && source.Drain == sink) {
            return sink is not ListOrArraySink ||
                   (source.SubjectCall.Args is [_, ConstInt count] && count.Value <= 64);
        }
        return true;
    }

    private LinqSink? CreateSink(CallInst call)
    {
        var method = call.Method;
        if (method.DeclaringType == t_Enumerable) {
#pragma warning disable format
            return method.Name switch {
                "ToList" or "ToArray"       => new ListOrArraySink(call),
                "ToHashSet"                 => new HashSetSink(call),
                "ToDictionary"              => new DictionarySink(call),
                "Aggregate"                 => new AggregationSink(call),
                "Count"                     => new CountSink(call),
                "First" or "FirstOrDefault" => new FindSink(call),
                "Any" or "All"              => new QuantifySink(call),
                _ => null
            };
#pragma warning restore format
        }
        if (method.Name == "GetEnumerator") {
            var declType = (method.DeclaringType as TypeSpec)?.Definition ?? method.DeclaringType;

            if (declType == t_IEnumerableOfT0.Definition || declType.Inherits(t_IEnumerableOfT0)) {
                return LoopSink.TryCreate(call);
            }
        }
        return null;
    }

    // UseRefs allows for overlapping queries to be expanded with no specific order.
    private LinqSourceNode CreateStage(UseRef sourceRef, LinqStageNode drain)
    {
        var source = sourceRef.Operand;

        if (source is CallInst call && call.Method.DeclaringType == t_Enumerable) {
            if (call.Method.Name == "Range") {
                return new IntRangeSource(call, drain);
            }
#pragma warning disable format
            var node = call.Method.Name switch {
                "Select"        => new SelectStage(call, drain),
                "Where"         => new WhereStage(call, drain),
                "OfType"        => new OfTypeStage(call, drain),
                "Cast"          => new CastStage(call, drain),
                "SelectMany"    => new FlattenStage(call, drain),
                "Skip"          => new SkipStage(call, drain),
                "Take" when call.Method.ParamSig[1] == PrimType.Int32
                                => new TakeStage(call, drain),
                _ => default(LinqStageNode)
            };
#pragma warning restore format
            if (node != null) {
                return CreateStage(call.GetOperandRef(0), node);
            }
        }
        var type = source.ResultType;

        if (type is ArrayType || type.IsCorelibType(typeof(List<>)) || type.Kind == TypeKind.String) {
            return new MemorySource(sourceRef, drain);
        }
        return new EnumeratorSource(sourceRef, drain);
    }
}