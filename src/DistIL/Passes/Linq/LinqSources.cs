namespace DistIL.Passes.Linq;

using DistIL.IR.Intrinsics;
using DistIL.IR.Utils;

/// <summary> Source based a sequential memory location: Array, List&lt;T>, or string. </summary>
internal class MemorySource : LinqSourceNode
{
    public MemorySource(UseRef source, LinqStageNode sink)
        : base(sink, source) { }

    Value? _currPtr, _endPtr;

    protected override void EmitHead(LoopBuilder loop, out Value? count)
    {
        var source = PhysicalSource.Operand;
        var builder = loop.PreHeader;

        var (startPtr, _) = source.ResultType switch {
            ArrayType t => (
                CreateGetArrayDataRef(builder, source),
                count = builder.CreateConvert(builder.CreateArrayLen(source), PrimType.Int32)
            ),
            TypeSpec { Name: "List`1" } t => (
                CreateGetArrayDataRef(builder, builder.CreateFieldLoad(t.FindField("_items"), source)),
                count = builder.CreateFieldLoad(t.FindField("_size"), source)
            ),
            TypeDesc { Kind: TypeKind.String } => (
                builder.CreateCallVirt("GetPinnableReference", source),
                count = builder.CreateCallVirt("get_Length", source)
            )
        };
        startPtr.SetName("lq_startPtr");
        //T& endPtr = startPtr + (nuint)count * sizeof(T)
        _endPtr = builder.CreatePtrOffset(startPtr, count, signed: false).SetName("lq_endPtr");
        //T& currPtr = phi [PreHeader: startPtr], [Latch: {currPtr + sizeof(T)}]
        _currPtr = loop.CreateAccum(startPtr, currPtr => loop.Latch.CreatePtrIncrement(currPtr)).SetName("lq_currPtr");
    }
    
    private static Value CreateGetArrayDataRef(IRBuilder builder, Value array)
    {
        var elemType = ((ArrayType)array.ResultType).ElemType;
        var T0 = new GenericParamType(0, isMethodParam: true);

        var m_GetArrayDataRef = builder.Resolver
            .Import(typeof(System.Runtime.InteropServices.MemoryMarshal))
            .FindMethod("GetArrayDataReference", new MethodSig(T0.CreateByref(), new TypeSig[] { T0.CreateArray() }, numGenPars: 1))
            .GetSpec(new GenericContext(methodArgs: new[] { elemType }));

        return builder.CreateCall(m_GetArrayDataRef, array);
    }

    protected override Value EmitMoveNext(IRBuilder builder)
        => builder.CreateCmp(CompareOp.Ult, _currPtr!, _endPtr!); //ptr < endPtr

    protected override Value EmitCurrent(IRBuilder builder)
        => builder.CreatePtrLoad(_currPtr!); //*ptr
}
internal class EnumeratorSource : LinqSourceNode
{
    public EnumeratorSource(UseRef enumerable, LinqStageNode sink)
        : base(sink, enumerable) { }

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