namespace DistIL.Passes.Linq;

using DistIL.IR.Intrinsics;
using DistIL.IR.Utils;

internal class ArraySource : LinqSourceNode
{
    public Value Array => PhysicalSource.Operand;

    public ArraySource(UseRef array, LinqStageNode sink)
        : base(sink, array) { }

    protected override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        //lq_index < array.Length
        return builder.CreateSlt(currIndex, EmitSourceCount(builder));
    }
    protected override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        //array[lq_index]
        return builder.CreateArrayLoad(Array, currIndex);
    }
    protected override Value EmitSourceCount(IRBuilder builder)
    {
        return builder.CreateConvert(builder.CreateArrayLen(Array), PrimType.Int32);
    }
}
internal class ListSource : LinqSourceNode
{
    public Value List => PhysicalSource.Operand;

    public ListSource(UseRef list, LinqStageNode sink)
        : base(sink, list) { }

    Value? _items, _count;

    protected override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        //lq_index < list.Count
        return builder.CreateSlt(currIndex, EmitSourceCount(builder));
    }
    protected override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        //list[lq_index]
        if (_items != null) {
            return builder.CreateArrayLoad(_items, currIndex);
        }
        var listType = List.ResultType;
        var getter = listType.FindMethod("get_Item", new MethodSig(listType.GenericParams[0], new TypeSig[] { PrimType.Int32 }));
        return builder.CreateCallVirt(getter, List, currIndex);
    }
    protected override Value EmitSourceCount(IRBuilder builder)
    {
        return _count ?? builder.CreateCallVirt("get_Count", List);
    }

    protected override void EmitHead(IRBuilder builder)
    {
        var type = List.ResultType;

        if (type.IsCorelibType(typeof(List<>))) {
            _items = builder.CreateFieldLoad(type.FindField("_items"), List);
            _count = builder.CreateFieldLoad(type.FindField("_size"), List);
        }
    }
}
internal class EnumeratorSource : LinqSourceNode
{
    private Value? _enumerator;
    private TypeDesc? _enumeratorType;

    public EnumeratorSource(UseRef enumerable, LinqStageNode sink)
        : base(sink, enumerable) { }

    protected override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        var method = _enumeratorType!.FindMethod("MoveNext", searchBaseAndItfs: true);
        return builder.CreateCallVirt(method, _enumerator);
    }
    protected override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        var method = _enumeratorType!.FindMethod("get_Current", searchBaseAndItfs: true);
        return builder.CreateCallVirt(method, _enumerator);
    }
    protected override void EmitHead(IRBuilder builder)
    {
        var source = PhysicalSource.Operand;
        var sourceType = source.ResultType;

        //This can still potentially change behavior (if the box is used somewhere else and GetEnumerator() mutates),
        //but we don't really care about poorly written implementations.
        if (source.Is(CilIntrinsicId.Box, out var boxed)) {
            sourceType = (TypeDesc)boxed.Args[0];
            source = builder.CreateIntrinsic(CilIntrinsic.UnboxRef, sourceType, boxed);
        }
        var method = sourceType.FindMethod("GetEnumerator", searchBaseAndItfs: true);
        _enumerator = builder.CreateCallVirt(method, source);
        _enumeratorType = _enumerator.ResultType;

        //If the enumerator itself is a struct, we need to copy it to a new variable and use its address instead
        if (_enumeratorType.IsValueType) {
            var slot = new Variable(_enumeratorType, "lq_EnumerSrcTmp", exposed: true);
            builder.CreateVarStore(slot, _enumerator);
            _enumerator = builder.CreateVarAddr(slot);
        }
    }
}