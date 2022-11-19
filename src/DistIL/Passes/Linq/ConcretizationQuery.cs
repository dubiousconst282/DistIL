namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

internal class ConcretizationQuery : LinqQuery
{
    public ConcretizationQuery(CallInst call, LinqStageNode pipeline)
        : base(call, pipeline) { }

    public override bool Emit()
    {
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
        //  goto BodyN
        //BodyN
        //  goto Latch
        //Latch:
        //  int nextIdx = add currIdx, 1
        //  goto Header
        //Exit:
        //  var result = WrapContainer(container)
        var preHeader = NewBlock("PreHeader");
        var header = NewBlock("Header");
        var body = NewBlock("Body");
        var latch = NewBlock("Latch");
        var exit = NewBlock("Exit");

        Pipeline.EmitHead(preHeader);
        var estimCount = Pipeline.EmitSourceCount(preHeader);
        var container = AllocContainer(preHeader, estimCount);
        preHeader.SetBranch(header.Block);

        var currIdx = header.CreatePhi(PrimType.Int32).SetName("currIdx");
        var hasNext = Pipeline.EmitMoveNext(header, currIdx);
        header.SetBranch(hasNext, body.Block, exit.Block);

        var currItem = Pipeline.EmitCurrent(body, currIdx, latch.Block);
        AppendItem(body, container, currItem);
        body.SetBranch(latch.Block);

        var nextIdx = latch.CreateAdd(currIdx, ConstInt.CreateI(1)).SetName("nextIdx");
        currIdx.AddArg((preHeader.Block, ConstInt.CreateI(0)), (latch.Block, nextIdx));
        latch.SetBranch(header.Block);

        var wrappedContainer = WrapContainer(exit, container);
        var newBlock = SubjectCall.Block.Split(SubjectCall, branchTo: preHeader.Block);
        exit.SetBranch(newBlock);

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
        var method = container.ResultType.FindMethod("Add", throwIfNotFound: true);
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
        var ctor = type.FindMethod(
            ".ctor", new MethodSig(PrimType.Void, sig),
            throwIfNotFound: true
        );
        return builder.CreateNewObj(ctor, args.ToArray());
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
    protected override Value WrapContainer(IRBuilder builder, Value container)
    {
        var method = container.ResultType.FindMethod("ToArray", throwIfNotFound: true);
        return builder.CreateCallVirt(method, container);
    }
}
internal class DictionaryConcretizationQuery : ConcretizationQuery
{
    public DictionaryConcretizationQuery(CallInst call, LinqStageNode pipeline)
        : base(call, pipeline) { }

    protected override void AppendItem(IRBuilder builder, Value container, Value currItem)
    {
        var method = container.ResultType.FindMethod("Add", throwIfNotFound: true);
        var key = builder.CreateLambdaInvoke(SubjectCall.Args[1], currItem);
        var value = currItem;
        if (SubjectCall.Args[2].ResultType.Name != "IEqualityComparer`1") {
            value = builder.CreateLambdaInvoke(SubjectCall.Args[2], currItem);
        }
        builder.CreateCallVirt(method, container, key, value);
    }
}