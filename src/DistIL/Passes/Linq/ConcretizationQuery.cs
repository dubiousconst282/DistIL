namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

internal class ConcretizationQuery : LinqQuery
{
    public ConcretizationQuery(CallInst call, LinqStageNode pipeline)
        : base(call, pipeline) { }

    public override void Emit()
    {
        //The main loop looks like this:
        //  PreHeader:
        //    var result = new Container(Source.GetCount())
        //    ...
        //    goto Header
        //  Header:
        //    int currIdx = phi [PreHeader -> 0, Latch -> nextIdx]
        //    bool hasNext = Source.MoveNext()
        //    goto hasNext ? Body1 : PreExit
        //  Body1:
        //    T currItem = Source.Current()
        //    result.Add(currItem)
        //  BodyN:
        //    goto Latch
        //  Latch:
        //    int nextIdx = add currIdx, 1
        //    goto Header
        //  Exit:
        //    ...
        var preHeader = NewBlock("PreHeader");
        var header = NewBlock("Header");
        var body = NewBlock("Body");
        var exit = NewBlock("Exit");

        var estimCount = Pipeline.EmitEstimCount(preHeader);
        var container = AllocContainer(preHeader, estimCount);
        preHeader.SetBranch(header.Block);

        var currIndex = header.CreatePhi(PrimType.Int32).SetName("currIdx");
        var hasNext = Pipeline.EmitMoveNext(header, currIndex);
        header.SetBranch(hasNext, body.Block, exit.Block);

        var latch = NewBlock("Latch");
        var nextIdx = latch.CreateAdd(currIndex, ConstInt.CreateI(1)).SetName("nextIdx");
        currIndex.AddArg((preHeader.Block, ConstInt.CreateI(0)), (latch.Block, nextIdx));
        latch.SetBranch(header.Block);

        var currItem = Pipeline.EmitCurrent(body, currIndex, latch.Block);
        AppendResult(body, container, currItem);
        body.SetBranch(latch.Block);

        var wrappedContainer = WrapContainer(exit, container);

        var newBlock = SubjectCall.Block.Split(SubjectCall, branchTo: preHeader.Block);
        exit.SetBranch(newBlock);

        SubjectCall.ReplaceWith(wrappedContainer);
        Pipeline.DeleteSubject();
    }

    protected virtual Value AllocContainer(IRBuilder builder, Value? count)
    {
        return AllocContainer(builder, count, (TypeDefOrSpec)SubjectCall.ResultType);
    }
    protected virtual void AppendResult(IRBuilder builder, Value container, Value currItem)
    {
        AppendResult(builder, container, currItem, (TypeDefOrSpec)container.ResultType);
    }
    protected virtual Value WrapContainer(IRBuilder builder, Value container)
    {
        return container;
    }

    protected Value AllocContainer(IRBuilder builder, Value? count, TypeDefOrSpec type)
    {
        var args = new List<Value>();

        if (count != null) {
            args.Add(count);
        }
        if (type.Name is "Dictionary`2" or "HashSet`1" && SubjectCall.Args[^1].ResultType.Name is "IEqualityComparer`1") {
            args.Add(SubjectCall.Args[^1]);
        }
        var ctor = type.FindMethod(
            ".ctor", new MethodSig(PrimType.Void, args.Select(a => (TypeSig)a.ResultType).ToList()),
            throwIfNotFound: true
        );
        return builder.CreateNewObj(ctor, args.ToArray());
    }
    protected void AppendResult(IRBuilder builder, Value container, Value currItem, TypeDefOrSpec containerType)
    {
        var method = containerType.FindMethod(
            "Add", new MethodSig(PrimType.Void, new TypeSig[] { containerType.GenericParams[0] }),
            throwIfNotFound: true
        );
        builder.CreateCallVirt(method, container, currItem);
    }
}
internal class ArrayConcretizationQuery : ConcretizationQuery
{
    public ArrayConcretizationQuery(CallInst call, LinqStageNode pipeline) : base(call, pipeline)
    {
    }

    protected override Value AllocContainer(IRBuilder builder, Value? count)
    {
        var resolver = Method.Definition.Module.Resolver;

        var arrayType = (ArrayType)SubjectCall.ResultType;
        var listType = (TypeDef)resolver.Import(typeof(List<>), throwIfNotFound: true);
        var type = listType.GetSpec(ImmutableArray.Create(arrayType.ElemType));

        return base.AllocContainer(builder, count, type);
    }
    protected override void AppendResult(IRBuilder builder, Value container, Value currItem)
    {
        base.AppendResult(builder, container, currItem, (TypeDefOrSpec)container.ResultType);
    }
    protected override Value WrapContainer(IRBuilder builder, Value container)
    {
        var containerType = (TypeDefOrSpec)container.ResultType;

        var method = containerType.FindMethod(
            "ToArray", new MethodSig(containerType.GenericParams[0].CreateArray(), Array.Empty<TypeSig>()),
            throwIfNotFound: true
        );
        return builder.CreateCallVirt(method, container);
    }
}