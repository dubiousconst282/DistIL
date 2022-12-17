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
        var queries = new List<LinqSourceNode>();

        foreach (var inst in ctx.Method.Instructions().OfType<CallInst>()) {
            if (CreateQuery(inst) is { } node) {
                queries.Add(node);
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

    private LinqSourceNode? CreateQuery(CallInst call)
    {
        var method = call.Method;
        if (method.DeclaringType == t_Enumerable) {
            return method.Name switch {
                "ToList" or "ToHashSet"
                    => CreateQuery(call, new ConcretizationQuery(call)),
                "ToArray"
                    => CreateQuery(call, new ArrayConcretizationQuery(call)),
                "ToDictionary"
                    => CreateQuery(call, new DictionaryConcretizationQuery(call)),
                "Count" when call.NumArgs == 2 //avoid expanding {List|Array}.Count()
                    => CreateQuery(call, new CountQuery(call)),
                "Aggregate"
                    => CreateQuery(call, new AggregationQuery(call)),
                "First" or "FirstOrDefault"
                    => CreateQuery(call, new PeekFirstQuery(call)),
                _ => null
            };
        }
        //Uses: itr.MoveNext(), itr.get_Current(), [itr?.Dispose()]
        if (method.Name == "GetEnumerator" && call.NumUses is 2 or 4) {
            var declType = (method.DeclaringType as TypeSpec)?.Definition ?? method.DeclaringType;

            if (declType == t_IEnumerableOfT0.Definition || declType.Inherits(t_IEnumerableOfT0)) {
                //return CreateQuery(call, pipe => new ConsumedQuery(call, pipe));
            }
        }
        return null;
    }

    private LinqSourceNode? CreateQuery(CallInst call, LinqQuery query, bool profitableIfEnumerator = false)
    {
        var root = CreatePipe(call.GetOperandRef(0), query);
        if (root is EnumeratorSource && root.Sink == query && !profitableIfEnumerator) {
            return null;
        }
        return root;
    }

    //UseRefs allows for overlapping queries to be expanded with no specific order.
    private LinqSourceNode CreatePipe(UseRef sourceRef, LinqStageNode sink)
    {
        var source = sourceRef.Operand;

        if (source is CallInst call && call.Method.DeclaringType == t_Enumerable) {
            var node = call.Method.Name switch {
                "Select"        => new SelectStage(call, sink),
                "Where"         => new WhereStage(call, sink),
                "OfType"        => new OfTypeStage(call, sink),
                "Cast"          => new CastStage(call, sink),
                //TODO: SelectMany stage needs to properly handle accum vars when nesting LoopBuilders
                //"SelectMany"    => new FlattenStage(call, sink),
                _ => default(LinqStageNode)
            };
            if (node != null) {
                return CreatePipe(call.GetOperandRef(0), node);
            }
        }
        if (source.ResultType is ArrayType) {
            return new ArraySource(sourceRef, sink);
        }
        if (source.ResultType is TypeSpec spec && spec.Definition.Inherits(t_IListOfT0)) {
            return new ListSource(sourceRef, sink);
        }
        return new EnumeratorSource(sourceRef, sink);
    }
}