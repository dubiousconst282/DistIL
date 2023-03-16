namespace DistIL.Passes.Linq;

using DistIL.IR.Intrinsics;
using DistIL.IR.Utils;

/// <summary> Source based on a continuous memory location: Array, List&lt;T>, or string. </summary>
internal class MemorySource : LinqSourceNode
{
    public MemorySource(UseRef source, LinqStageNode drain)
        : base(drain, source) { }

    Value? _currPtr, _endPtr;

    protected override void EmitHead(LoopBuilder loop, out Value? count)
    {
        //T& startPtr = call MemoryMarshal.GetArrayDataReference<T>(T[]: source)  //or akin.
        //T& endPtr = startPtr + (nint)count * sizeof(T)
        (_currPtr, _endPtr, count) = LoopStrengthReduction.CreateGetDataPtrRange(loop.PreHeader, PhysicalSource.Operand);

        //T& currPtr = phi [PreHeader: startPtr], [Latch: {currPtr + sizeof(T)}]
        _currPtr = loop.CreateAccum(_currPtr, currPtr => loop.Latch.CreatePtrIncrement(currPtr)).SetName("lq_currPtr");
    }

    protected override Value EmitMoveNext(IRBuilder builder)
        => builder.CreateUlt(_currPtr!, _endPtr!); //ptr < endPtr

    protected override Value EmitCurrent(IRBuilder builder)
        => builder.CreateLoad(_currPtr!); //*ptr
}
internal class EnumeratorSource : LinqSourceNode
{
    public EnumeratorSource(UseRef enumerable, LinqStageNode drain)
        : base(drain, enumerable) { }

    Value? _enumerator;

    protected override void EmitHead(LoopBuilder loop, out Value? count)
    {
        var builder = loop.PreHeader;
        var source = PhysicalSource.Operand;
        var sourceType = source.ResultType;

        //TODO: This can still potentially change behavior (if the box is used somewhere else and GetEnumerator() mutates)
        if (source.Is(CilIntrinsicId.Box, out var boxed)) {
            sourceType = (TypeDesc)boxed.Args[0];
            source = builder.CreateIntrinsic(CilIntrinsic.UnboxRef, sourceType, boxed);
        }
        var method = sourceType.FindMethod("GetEnumerator", searchBaseAndItfs: true);
        _enumerator = builder.CreateCallVirt(method, source);

        //If the enumerator itself is a struct, we need to copy it to a new variable and use its address instead
        if (_enumerator.ResultType.IsValueType) {
            var slot = new Variable(_enumerator.ResultType, "lq_EnumerSrcTmp", exposed: true);
            builder.CreateVarStore(slot, _enumerator);
            _enumerator = builder.CreateVarAddr(slot);
        }
        count = null;
    }
    protected override Value EmitMoveNext(IRBuilder builder)
        => builder.CreateCallVirt("MoveNext", _enumerator!);

    protected override Value EmitCurrent(IRBuilder builder)
        => builder.CreateCallVirt("get_Current", _enumerator!);
}
internal class IntRangeSource : LinqSourceNode
{
    public IntRangeSource(CallInst subjectCall, LinqStageNode drain)
        : base(drain, default, subjectCall) { }

    Value? _index, _end;

    protected override void EmitHead(LoopBuilder loop, out Value? count)
    {
        var start = SubjectCall.Args[0];
        count = SubjectCall.Args[1];

        var builder = loop.PreHeader;

        //if (count < 0 | (sext(start) + sext(count)) > int.MaxValue) throw;
        builder.Throw(
            typeof(ArgumentOutOfRangeException),
            builder.CreateOr(
                builder.CreateSlt(count, ConstInt.CreateI(0)),
                builder.CreateUgt(
                    builder.CreateAdd(
                        builder.CreateConvert(start, PrimType.Int64),
                        builder.CreateConvert(count, PrimType.Int64)),
                    ConstInt.CreateL(int.MaxValue))));

        //int index = phi [PreHeader: start], [Latch: {index + 1}]
        _index = loop.CreateAccum(start, curr => loop.Latch.CreateAdd(curr, ConstInt.CreateI(1))).SetName("lq_rangeidx");
        _end = builder.CreateAdd(start, count);
    }

    protected override Value EmitMoveNext(IRBuilder builder)
        => builder.CreateSlt(_index!, _end!);

    protected override Value EmitCurrent(IRBuilder builder)
        => _index!;
}