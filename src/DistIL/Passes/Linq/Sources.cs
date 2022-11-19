namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

internal class ArraySource : LinqStageNode
{
    public Value Array { get; }

    public ArraySource(Value array)
        => Array = array;

    public override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        //lq_index < array.Length
        return builder.CreateSlt(currIndex, EmitSourceCount(builder));
    }
    public override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        //array[lq_index]
        return builder.CreateArrayLoad(Array, currIndex);
    }
    public override Value EmitSourceCount(IRBuilder builder)
    {
        return builder.CreateConvert(builder.CreateArrayLen(Array), PrimType.Int32);
    }
}
internal class ListSource : LinqStageNode
{
    public Value List { get; }
    public TypeDefOrSpec Type => (TypeDefOrSpec)List.ResultType;

    public ListSource(Value list)
        => List = list;

    public override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        //lq_index < list.Count
        return builder.CreateSlt(currIndex, EmitSourceCount(builder));
    }
    public override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        //list[lq_index]
        var getter = Type.FindMethod(
            "get_Item", new MethodSig(Type.GenericParams[0], new TypeSig[] { PrimType.Int32 }),
            throwIfNotFound: true
        );
        return builder.CreateCallVirt(getter, List, currIndex);
    }
    public override Value EmitSourceCount(IRBuilder builder)
    {
        var getter = Type.FindMethod("get_Count", throwIfNotFound: true);
        return builder.CreateCallVirt(getter, List);
    }
}
internal class EnumeratorSource : LinqStageNode
{
    public Value Enumerable { get; }
    private CallInst? _enumerator;

    public EnumeratorSource(Value enumerable)
        => Enumerable = enumerable;

    public override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        var method = _enumerator!.ResultType.FindMethod("MoveNext", searchBaseAndItfs: true, throwIfNotFound: true);
        return builder.CreateCallVirt(method, _enumerator);
    }
    public override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        var method = _enumerator!.ResultType.FindMethod("get_Current", searchBaseAndItfs: true, throwIfNotFound: true);
        return builder.CreateCallVirt(method, _enumerator);
    }
    public override void EmitHead(IRBuilder builder)
    {
        var method = Enumerable.ResultType.FindMethod("GetEnumerator", searchBaseAndItfs: true, throwIfNotFound: true);
        _enumerator = builder.CreateCallVirt(method, Enumerable);
    }
}
