namespace DistIL.Passes;

using DistIL.Passes.Linq;

public class ExpandLinq : MethodPass
{
    readonly TypeDesc t_Enumerable, t_IListOfT;

    public ExpandLinq(ModuleDef mod)
    {
        t_Enumerable = mod.Resolver.Import(typeof(Enumerable), throwIfNotFound: true);
        t_IListOfT = mod.Resolver.Import(typeof(IList<>), throwIfNotFound: true);
    }

    public override void Run(MethodTransformContext ctx)
    {
        var queries = new List<LinqQuery>();

        foreach (var inst in ctx.Method.Instructions().OfType<CallInst>()) {
            if (CreateQuery(inst) is { } query) {
                queries.Add(query);
            }
        }

        foreach (var query in queries) {
            query.Emit();
        }
        if (queries.Count == 0) {
            ctx.PreserveAll();
        }
    }

    private LinqQuery? CreateQuery(CallInst call)
    {
        var method = call.Method;
        if (method.DeclaringType == t_Enumerable) {
            if (method.Name is "ToArray") {
                return CreateQuery(call, pipe => new ArrayConcretizationQuery(call, pipe));
            }
            if (method.Name is "ToList" or "ToHashSet") {
                return CreateQuery(call, pipe => new ConcretizationQuery(call, pipe));
            }
        }
        return null;
    }

    private LinqQuery CreateQuery(CallInst call, Func<LinqStageNode, LinqQuery> factory)
    {
        var pipe = CreateStage(call.Args[0]);
        return factory(pipe);

        LinqStageNode CreateStage(Value source)
        {
            if (source is CallInst call && call.Method.DeclaringType == t_Enumerable) {
                return call.Method.Name switch {
                    "Select" => new SelectStage(call, CreateStage(call.Args[0])),
                    "Where" => new WhereStage(call, CreateStage(call.Args[0])),
                    _ => CreateEnumeratorSource(call)
                };
            } else if (source.ResultType is ArrayType) {
                return new ArraySource(source);
            } else if (source.ResultType.Inherits(t_IListOfT)) {
                return new ListSource(source);
            }
            return CreateEnumeratorSource(source);
        }
        LinqStageNode CreateEnumeratorSource(Value source)
        {
            var method = source.ResultType.FindMethod("GetEnumerator", throwIfNotFound: true);
            var enumerator = new CallInst(method, new[] { source }, isVirtual: true);
            enumerator.InsertBefore(call);
            return new EnumeratorSource(enumerator);
        }
    }
}