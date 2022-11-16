namespace DistIL.Passes.Linq;

using System.Collections;

using DistIL.IR.Utils;

internal class ArraySource : LinqStageNode
{
    public Value Array { get; }

    public ArraySource(Value array)
        => Array = array;

    public override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        //lq_index < array.Length
        return builder.CreateSlt(currIndex, EmitEstimCount(builder));
    }
    public override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        //array[lq_index]
        return builder.CreateArrayLoad(Array, currIndex);
    }
    public override Value EmitEstimCount(IRBuilder builder)
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
        return builder.CreateSlt(currIndex, EmitEstimCount(builder));
    }
    public override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        //list[lq_index]
        var getter = Type.FindMethod(
            "get_Item", new MethodSig(Type.GenericParams[0], new TypeSig[] { PrimType.Int32 }),
            throwIfNotFound: true
        );
        return builder.CreateCallVirt(getter, List);
    }
    public override Value EmitEstimCount(IRBuilder builder)
    {
        var getter = Type.FindMethod("get_Count", throwIfNotFound: true);
        return builder.CreateCallVirt(getter, List);
    }
}
internal class EnumeratorSource : LinqStageNode
{
    public Value Enumerator { get; }
    public TypeDefOrSpec Type => (TypeDefOrSpec)Enumerator.ResultType;

    public EnumeratorSource(Value enumerator)
        => Enumerator = enumerator;

    public override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        var t_IEnumerator = Type.Module.Resolver.Import(typeof(IEnumerator), throwIfNotFound: true);
        var method = t_IEnumerator.FindMethod("MoveNext", throwIfNotFound: true);
        return builder.CreateCallVirt(method, Enumerator);
    }
    public override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        var method = Type.FindMethod("get_Current", throwIfNotFound: true);
        return builder.CreateCallVirt(method, Enumerator);
    }
}
