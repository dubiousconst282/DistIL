namespace DistIL.Passes.Linq;

using DistIL.IR.Intrinsics;
using DistIL.IR.Utils;

internal class ArraySource : LinqSourceNode
{
    public ArraySource(UseRef array, LinqStageNode sink)
        : base(sink, array) { }

    Value? _currPtr, _endPtr;

    protected override void EmitHead(LoopBuilder loop, out Value? count)
    {
        var source = PhysicalSource.Operand;
        var method = PhysicalSource.Parent.Block.Method;
        var resolver = method.Definition.Module.Resolver;

        var builder = loop.PreHeader;

        var (elemType, array, _) = source.ResultType switch {
            ArrayType t => (
                t.ElemType,
                source,
                count = builder.CreateConvert(builder.CreateArrayLen(source), PrimType.Int32)
            ),
            TypeSpec { Name: "List`1" } t => (
                t.GenericParams[0],
                builder.CreateFieldLoad(t.FindField("_items"), source),
                count = builder.CreateFieldLoad(t.FindField("_size"), source)
            )
        };
        var T0 = new GenericParamType(0, isMethodParam: true);
        var m_GetArrayDataRef = resolver
            .Import(typeof(System.Runtime.InteropServices.MemoryMarshal))
            .FindMethod("GetArrayDataReference", new MethodSig(T0.CreateByref(), new TypeSig[] { T0.CreateArray() }, numGenPars: 1))
            .GetSpec(new GenericContext(methodArgs: new TypeDesc[] { elemType }));

        var startPtr = builder.CreateCall(m_GetArrayDataRef, array).SetName("lq_startPtr");
        //T& endPtr = startPtr + (nuint)count * sizeof(T)
        _endPtr = builder.CreatePtrOffset(startPtr, count, signed: false).SetName("lq_endPtr");
        //T& currPtr = phi [PH: startPtr], [Latch: {currPtr + sizeof(T)}]
        _currPtr = loop.CreateAccum(startPtr, currPtr => loop.Latch.CreatePtrIncrement(currPtr)).SetName("lq_currPtr");
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
    TypeDesc? _enumeratorType;

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
        _enumeratorType = _enumerator.ResultType;

        //If the enumerator itself is a struct, we need to copy it to a new variable and use its address instead
        if (_enumeratorType.IsValueType) {
            var slot = new Variable(_enumeratorType, "lq_EnumerSrcTmp", exposed: true);
            builder.CreateVarStore(slot, _enumerator);
            _enumerator = builder.CreateVarAddr(slot);
        }
        count = null;
    }
    protected override Value EmitMoveNext(IRBuilder builder)
    {
        var method = _enumeratorType!.FindMethod("MoveNext", searchBaseAndItfs: true);
        return builder.CreateCallVirt(method, _enumerator);
    }
    protected override Value EmitCurrent(IRBuilder builder)
    {
        var method = _enumeratorType!.FindMethod("get_Current", searchBaseAndItfs: true);
        return builder.CreateCallVirt(method, _enumerator);
    }
}