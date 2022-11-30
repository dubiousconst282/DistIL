namespace DistIL.Passes.Linq;

using DistIL.IR.Intrinsics;
using DistIL.IR.Utils;

internal class ArraySource : LinqSourceNode
{
    public ArraySource(Value array)
        : base(physicalSource: array) { }

    public override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        //lq_index < array.Length
        return builder.CreateSlt(currIndex, EmitSourceCount(builder));
    }
    public override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        //array[lq_index]
        return builder.CreateArrayLoad(PhysicalSource, currIndex);
    }
    public override Value EmitSourceCount(IRBuilder builder)
    {
        return builder.CreateConvert(builder.CreateArrayLen(PhysicalSource), PrimType.Int32);
    }
}
internal class ListSource : LinqSourceNode
{
    public TypeDefOrSpec Type => (TypeDefOrSpec)PhysicalSource.ResultType;

    public ListSource(Value list)
        : base(physicalSource: list) { }

    public override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        //lq_index < list.Count
        return builder.CreateSlt(currIndex, EmitSourceCount(builder));
    }
    public override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        //list[lq_index]
        var getter = Type.FindMethod("get_Item", new MethodSig(Type.GenericParams[0], new TypeSig[] { PrimType.Int32 }));
        return builder.CreateCallVirt(getter, PhysicalSource, currIndex);
    }
    public override Value EmitSourceCount(IRBuilder builder)
    {
        var getter = Type.FindMethod("get_Count");
        return builder.CreateCallVirt(getter, PhysicalSource);
    }
}
internal class EnumeratorSource : LinqSourceNode
{
    private Value? _enumerator;
    private TypeDesc? _enumeratorType;

    public EnumeratorSource(Value enumerable)
        : base(physicalSource: enumerable) { }

    public override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        var method = _enumeratorType!.FindMethod("MoveNext", searchBaseAndItfs: true);
        return builder.CreateCallVirt(method, _enumerator);
    }
    public override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        var method = _enumeratorType!.FindMethod("get_Current", searchBaseAndItfs: true);
        return builder.CreateCallVirt(method, _enumerator);
    }
    public override void EmitHead(IRBuilder builder)
    {
        var source = PhysicalSource;
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