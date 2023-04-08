namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

internal abstract class ConcretizationSink : LinqSink
{
    public ConcretizationSink(CallInst call)
        : base(call) { }

    Value? _container;

    public override void EmitHead(IRBuilder builder, EstimatedSourceLen estimLen)
    {
        _container = AllocContainer(builder, estimLen);
    }
    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        AppendItem(builder, _container!, currItem, loopData);
    }
    public override void EmitTail(IRBuilder builder)
    {
        var result = WrapContainer(builder, _container!);
        SubjectCall.ReplaceUses(result);
    }

    protected virtual Value AllocContainer(IRBuilder builder, EstimatedSourceLen estimLen)
        => AllocKnownCollection(builder, estimLen, SubjectCall.ResultType);

    protected virtual void AppendItem(IRBuilder builder, Value container, Value currItem, BodyLoopData loopData)
        => builder.CreateCallVirt("Add", container, currItem);
    protected virtual Value WrapContainer(IRBuilder builder, Value container) => container;

    //Allocatess a new List/HashSet/Dictionary as dictated by `type`
    protected Value AllocKnownCollection(IRBuilder builder, EstimatedSourceLen estimLen, TypeDesc type)
    {
        Debug.Assert(type.Name is "List`1" or "HashSet`1" or "Dictionary`2");

        var args = new List<Value>();
        var sig = new List<TypeSig>();

        if (estimLen.Length != null && !estimLen.IsOverEstimation) {
            args.Add(estimLen.Length);
            sig.Add(PrimType.Int32);
        }
        var lastParType = SubjectCall.Method.ParamSig[^1].Type;

        if (type.Name is "Dictionary`2" or "HashSet`1" && lastParType.Name is "IEqualityComparer`1") {
            args.Add(SubjectCall.Args[^1]);
            sig.Add(lastParType.GetUnboundSpec());
        }
        var ctor = type.FindMethod(".ctor", new MethodSig(PrimType.Void, sig));
        return builder.CreateNewObj(ctor, args.ToArray());
    }
}
internal class ListOrArraySink : ConcretizationSink
{
    public ListOrArraySink(CallInst call)
        : base(call) { }

    Value? _index;

    protected override Value AllocContainer(IRBuilder builder, EstimatedSourceLen estimLen)
    {
        var elemType = SubjectCall.ResultType;
        elemType = (elemType as ArrayType)?.ElemType ?? elemType.GenericParams[0];

        if (estimLen.IsExact) {
            return builder.CreateNewArray(elemType, estimLen.Length);
        }
        var listGenType = (TypeDef)builder.Resolver.Import(typeof(List<>));
        return base.AllocKnownCollection(builder, estimLen, listGenType.GetSpec(elemType));
    }

    protected override void AppendItem(IRBuilder builder, Value container, Value currItem, BodyLoopData loopData)
    {
        if (container.ResultType is ArrayType) {
            _index = loopData.CreateAccum(ConstInt.CreateI(0), curr => builder.CreateAdd(curr, ConstInt.CreateI(1)));
            builder.CreateArrayStore(container, _index, currItem, inBounds: true);
        } else {
            base.AppendItem(builder, container, currItem, loopData);
        }
    }

    protected override Value WrapContainer(IRBuilder builder, Value container)
    {
        if (container.ResultType == SubjectCall.ResultType) {
            return container;
        }
        if (SubjectCall.Method.Name == "ToArray") {
            return builder.CreateCallVirt("ToArray", container);
        }
        if (SubjectCall.Method.Name == "ToList") {
            var list = AllocKnownCollection(builder, default, SubjectCall.ResultType);
            builder.CreateFieldStore("_items", list, container);
            builder.CreateFieldStore("_size", list, _index!);
            return list;
        }
        throw new UnreachableException();
    }
}
internal class HashSetSink : ConcretizationSink
{
    public HashSetSink(CallInst call)
        : base(call) { }
}
internal class DictionarySink : ConcretizationSink
{
    public DictionarySink(CallInst call)
        : base(call) { }

    protected override void AppendItem(IRBuilder builder, Value container, Value currItem, BodyLoopData loopData)
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