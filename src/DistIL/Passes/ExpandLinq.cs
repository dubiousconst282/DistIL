namespace DistIL.Passes;

using DistIL.Passes.Linq;

public class ExpandLinq : MethodPass
{
    readonly TypeDefOrSpec t_Enumerable, t_IListOfT0, t_IEnumerableOfT0;

    public ExpandLinq(ModuleDef mod)
    {
        t_Enumerable = mod.Resolver.Import(typeof(Enumerable));
        t_IListOfT0 = mod.Resolver.Import(typeof(IList<>)).GetSpec(default);
        t_IEnumerableOfT0 = mod.Resolver.Import(typeof(IEnumerable<>)).GetSpec(default);
    }

    public override void Run(MethodTransformContext ctx)
    {
        var queries = new List<LinqQuery>();

        foreach (var inst in ctx.Method.Instructions().OfType<CallInst>()) {
            if (CreateQuery(inst) is { } query) {
                queries.Add(query);
            }
        }

        //Overlapping queries must be expanded in a specific order, because the SubjectCall instruction of
        //a query source won't be updated after a dependency query is expanded.
        //We could sort them based on the dominator tree order or just by traversing them using some sort of DFS.
        //These queries seem to be quite rare, so we'll just give up for now. 
        if (queries.Any(q1 => queries.Any(q2 => GetSource(q2).PhysicalSource == q1.SubjectCall))) {
            ctx.PreserveAll();
            return;
        }

        foreach (var query in queries) {
            if (query.Emit()) {
                ctx.InvalidateAll();
            }
        }
    }

    private static LinqSourceNode GetSource(LinqQuery query)
    {
        var node = query.Pipeline;
        while (true) {
            if (node is LinqSourceNode src) {
                return src;
            }
            node = node.Source!;
        }
    }

    private LinqQuery? CreateQuery(CallInst call)
    {
        var method = call.Method;
        if (method.DeclaringType == t_Enumerable) {
            return method.Name switch {
                "ToList" or "ToHashSet"
                    => CreateQuery(call, pipe => new ConcretizationQuery(call, pipe)),
                "ToArray"
                    => CreateQuery(call, pipe => new ArrayConcretizationQuery(call, pipe)),
                "ToDictionary"
                    => CreateQuery(call, pipe => new DictionaryConcretizationQuery(call, pipe)),
                "Count" when call.NumArgs == 2 //avoid expanding {List|Array}.Count()
                    => CreateQuery(call, pipe => new CountQuery(call, pipe)),
                "Aggregate" when call.NumArgs >= 3 //unseeded aggregates are not supported
                    => CreateQuery(call, pipe => new AggregationQuery(call, pipe)),
                _ => null
            };
        }
        //Uses: itr.MoveNext(), itr.get_Current(), [itr?.Dispose()]
        if (method.Name == "GetEnumerator" && call.NumUses is 2 or 4) {
            var declType = (method.DeclaringType as TypeSpec)?.Definition ?? method.DeclaringType;

            if (declType == t_IEnumerableOfT0.Definition || declType.Inherits(t_IEnumerableOfT0)) {
                return CreateQuery(call, pipe => new ConsumedQuery(call, pipe));
            }
        }
        return null;
    }

    private LinqQuery? CreateQuery(CallInst call, Func<LinqStageNode, LinqQuery> factory, bool profitableIfEnumerator = false)
    {
        var pipe = CreateStage(call.Args[0]);
        if (pipe is EnumeratorSource && !profitableIfEnumerator) {
            return null;
        }
        return factory(pipe);
    }

    private LinqStageNode CreateStage(Value source)
    {
        if (source is CallInst call && call.Method.DeclaringType == t_Enumerable) {
            var stage = call.Method.Name switch {
                "Select"    => new SelectStage(call,    CreateStage(call.Args[0])),
                "Where"     => new WhereStage(call,     CreateStage(call.Args[0])),
                "OfType"    => new OfTypeStage(call,    CreateStage(call.Args[0])),
                "Cast"      => new CastStage(call,      CreateStage(call.Args[0])),
                _ => default(LinqStageNode)
            };
            if (stage != null) {
                return stage;
            }
        }
        if (source.ResultType is ArrayType) {
            return new ArraySource(source);
        }
        if (source.ResultType is TypeSpec spec && spec.Definition.Inherits(t_IListOfT0)) {
            return new ListSource(source);
        }
        return new EnumeratorSource(source);
    }
}