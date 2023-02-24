namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

internal class ConcretizationSink : LinqSink
{
    public ConcretizationSink(CallInst call)
        : base(call) { }

    Value? _container;

    public override void EmitHead(IRBuilder builder, Value? estimCount)
    {
        _container = AllocContainer(builder, estimCount);
    }
    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        AppendItem(builder, _container!, currItem);
    }
    public override void EmitTail(IRBuilder builder)
    {
        var result = WrapContainer(builder, _container!);
        SubjectCall.ReplaceUses(result);
    }

    protected virtual Value AllocContainer(IRBuilder builder, Value? count)
    {
        return AllocContainer(builder, count, (TypeDefOrSpec)SubjectCall.ResultType);
    }
    protected virtual void AppendItem(IRBuilder builder, Value container, Value currItem)
    {
        builder.CreateCallVirt("Add", container, currItem);
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
internal class ArraySink : ConcretizationSink
{
    public ArraySink(CallInst call)
        : base(call) { }

    protected override Value AllocContainer(IRBuilder builder, Value? count)
    {
        var arrayType = (ArrayType)SubjectCall.ResultType;
        var listType = (TypeDef)builder.Resolver.Import(typeof(List<>));
        var type = listType.GetSpec(ImmutableArray.Create(arrayType.ElemType));

        return base.AllocContainer(builder, count, type);
    }
    protected override Value WrapContainer(IRBuilder builder, Value container)
    {
        return builder.CreateCallVirt("ToArray", container);
    }
}
internal class DictionarySink : ConcretizationSink
{
    public DictionarySink(CallInst call)
        : base(call) { }

    protected override void AppendItem(IRBuilder builder, Value container, Value currItem)
    {
        var key = builder.CreateLambdaInvoke(SubjectCall.Args[1], currItem);
        var value = currItem;
        //Signature: ToDictionary(source, keySelector, [elementSelector], [comparer])
        if (SubjectCall.Args is [_, _, { ResultType.Name: not "IEqualityComparer`1" }, ..]) {
            value = builder.CreateLambdaInvoke(SubjectCall.Args[2], currItem);
        }
        builder.CreateCallVirt("Add", container, key, value);
    }
}