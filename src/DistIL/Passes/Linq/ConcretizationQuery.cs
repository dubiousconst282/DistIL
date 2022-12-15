namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

internal class ConcretizationQuery : LinqQuery
{
    public ConcretizationQuery(CallInst call, LinqStageNode pipeline)
        : base(call, pipeline) { }

    //Emit a loop like this:
    //
    //PreHeader:
    //  var container = new Container(Source.GetCount())
    //  ...
    //  goto Header
    //Header:
    //  int currIdx = phi [PreHeader -> 0, Latch -> nextIdx]
    //  bool hasNext = Source.MoveNext(currIdx)
    //  goto hasNext ? Body1 : Exit
    //Body1:
    //  T currItem = Source.Current()
    //  container.Add(currItem)
    //  goto cond ? BodyN : Latch
    //BodyN
    //  goto Latch
    //Latch:
    //  int nextIdx = add currIdx, 1
    //  goto Header
    //Exit:
    //  var result = WrapContainer(container)
    public override bool Emit()
    {
        var loop = new LoopBuilder(createBlock: name => NewBlock(name));
        var index = loop.CreateInductor().SetName("lq_index");

        Pipeline.EmitHead(loop.PreHeader);
        var maxCount = Pipeline.EmitSourceCount(loop.PreHeader);
        var container = AllocContainer(loop.PreHeader, maxCount);

        loop.Build(
            emitCond: header => Pipeline.EmitMoveNext(header, index),
            emitBody: body => {
                var currItem = Pipeline.EmitCurrent(body, index, loop.Latch.Block);
                AppendItem(body, container, currItem);
            }
        );
        var wrappedContainer = WrapContainer(loop.Exit, container);
        loop.InsertBefore(SubjectCall);

        SubjectCall.ReplaceWith(wrappedContainer);
        Pipeline.DeleteSubject();
        return true;
    }

    protected virtual Value AllocContainer(IRBuilder builder, Value? count)
    {
        return AllocContainer(builder, count, (TypeDefOrSpec)SubjectCall.ResultType);
    }
    protected virtual void AppendItem(IRBuilder builder, Value container, Value currItem)
    {
        var method = container.ResultType.FindMethod("Add");
        builder.CreateCallVirt(method, container, currItem);
    }
    protected virtual Value WrapContainer(IRBuilder builder, Value container)
    {
        return container;
    }

    protected Value AllocContainer(IRBuilder builder, Value? count, TypeDefOrSpec type)
    {
        var args = new List<Value>();
        var sig = new List<TypeSig>();

        if (count != null) {
            args.Add(count);
            sig.Add(PrimType.Int32);
        }
        var lastParType = SubjectCall.Method.ParamSig[^1].Type;
        if (type.Name is "Dictionary`2" or "HashSet`1" && lastParType.Name is "IEqualityComparer`1") {
            args.Add(SubjectCall.Args[^1]);
            sig.Add(lastParType);
        }
        var ctor = type.FindMethod(".ctor", new MethodSig(PrimType.Void, sig));
        return builder.CreateNewObj(ctor, args.ToArray());
    }
}
internal class ArrayConcretizationQuery : ConcretizationQuery
{
    public ArrayConcretizationQuery(CallInst call, LinqStageNode pipeline)
        : base(call, pipeline) { }

    protected override Value AllocContainer(IRBuilder builder, Value? count)
    {
        var resolver = Method.Definition.Module.Resolver;

        var arrayType = (ArrayType)SubjectCall.ResultType;
        var listType = (TypeDef)resolver.Import(typeof(List<>));
        var type = listType.GetSpec(ImmutableArray.Create(arrayType.ElemType));

        return base.AllocContainer(builder, count, type);
    }
    protected override Value WrapContainer(IRBuilder builder, Value container)
    {
        var method = container.ResultType.FindMethod("ToArray");
        return builder.CreateCallVirt(method, container);
    }
}
internal class DictionaryConcretizationQuery : ConcretizationQuery
{
    public DictionaryConcretizationQuery(CallInst call, LinqStageNode pipeline)
        : base(call, pipeline) { }

    protected override void AppendItem(IRBuilder builder, Value container, Value currItem)
    {
        var method = container.ResultType.FindMethod("Add");
        var key = builder.CreateLambdaInvoke(SubjectCall.Args[1], currItem);
        var value = currItem;
        //Signature: ToDictionary(source, keySelector, [elementSelector], [comparer])
        if (SubjectCall.Args is [_, _, { ResultType.Name: not "IEqualityComparer`1" }, ..]) {
            value = builder.CreateLambdaInvoke(SubjectCall.Args[2], currItem);
        }
        builder.CreateCallVirt(method, container, key, value);
    }
}