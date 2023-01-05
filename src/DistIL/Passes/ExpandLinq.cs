namespace DistIL.Passes;

using DistIL.Passes.Linq;

public class ExpandLinq : MethodPass
{
    readonly TypeDefOrSpec t_Enumerable, t_IEnumerableOfT0;

    public ExpandLinq(ModuleDef mod)
    {
        t_Enumerable = mod.Resolver.Import(typeof(Enumerable));
        t_IEnumerableOfT0 = mod.Resolver.Import(typeof(IEnumerable<>)).GetSpec(default);
    }

    public override void Run(MethodTransformContext ctx)
    {
        var queries = new List<LinqSourceNode>();

        foreach (var inst in ctx.Method.Instructions().OfType<CallInst>()) {
            if (CreatePipe(inst) is { } pipe) {
                queries.Add(pipe);
            }
        }

        foreach (var query in queries) {
            query.Emit();
            query.DeleteSubject();
        }
        if (queries.Count > 0) {
            ctx.Logger.Info($"Expanded {queries.Count} queries");
            ctx.InvalidateAll();
        }
    }

    private LinqSourceNode? CreatePipe(CallInst call)
    {
        var sink = CreateQuery(call);
        if (sink == null) {
            return null;
        }
        var source = CreateStage(call.GetOperandRef(0), sink);
        return IsProfitableToExpand(source, sink) ? source : null;
    }

    private static bool IsProfitableToExpand(LinqSourceNode source, LinqQuery query)
    {
        //Unfiltered Count() is definitely not profitable
        if (query.SubjectCall is { NumArgs: 1, Method.Name: "Count" }) {
            return false;
        }
        //Concretizing enumerator sources may not be profitable
        if (source is EnumeratorSource && source.Sink == query) {
            return query is not ConcretizationQuery;
        }
        return true;
    }

    private LinqQuery? CreateQuery(CallInst call)
    {
        var method = call.Method;
        if (method.DeclaringType == t_Enumerable) {
#pragma warning disable format
            return method.Name switch {
                "ToList" or "ToHashSet"     => new ConcretizationQuery(call),
                "ToArray"                   => new ArrayConcretizationQuery(call),
                "ToDictionary"              => new DictionaryConcretizationQuery(call),
                "Aggregate"                 => new AggregationQuery(call),
                "Count"                     => new CountQuery(call),
                "First" or "FirstOrDefault" => new FindFirstQuery(call),
                _ => null
            };
#pragma warning restore format
        }
        //Uses: itr.MoveNext(), itr.get_Current(), [itr?.Dispose()]
        if (method.Name == "GetEnumerator" && call.NumUses is 2 or 4) {
            var declType = (method.DeclaringType as TypeSpec)?.Definition ?? method.DeclaringType;

            if (declType == t_IEnumerableOfT0.Definition || declType.Inherits(t_IEnumerableOfT0)) {
                //return new ConsumedQuery(call, pipe);
            }
        }
        return null;
    }

    //UseRefs allows for overlapping queries to be expanded with no specific order.
    private LinqSourceNode CreateStage(UseRef sourceRef, LinqStageNode sink)
    {
        var source = sourceRef.Operand;

        if (source is CallInst call && call.Method.DeclaringType == t_Enumerable) {
#pragma warning disable format
            var node = call.Method.Name switch {
                "Select"        => new SelectStage(call, sink),
                "Where"         => new WhereStage(call, sink),
                "OfType"        => new OfTypeStage(call, sink),
                "Cast"          => new CastStage(call, sink),
                "Skip"          => new SkipStage(call, sink),
                "SelectMany"    => new FlattenStage(call, sink),
                _ => default(LinqStageNode)
            };
#pragma warning restore format
            if (node != null) {
                return CreateStage(call.GetOperandRef(0), node);
            }
        }
        var type = source.ResultType;

        if (type is ArrayType || type.IsCorelibType(typeof(List<>))) {
            return new ArraySource(sourceRef, sink);
        }
        return new EnumeratorSource(sourceRef, sink);
    }
}