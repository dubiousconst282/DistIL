namespace DistIL.Passes.Linq;

using DistIL.IR.Intrinsics;
using DistIL.IR.Utils;

internal class SelectStage : LinqStageNode
{
    public Value MapLambda => SubjectCall!.Args[1];

    public SelectStage(CallInst call, LinqStageNode? source)
        : base(call, source) { }

    public override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        return Source!.EmitMoveNext(builder, currIndex);
    }
    public override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        var currItem = Source!.EmitCurrent(builder, currIndex, skipBlock);
        return builder.CreateLambdaInvoke_ItemAndIndex(MapLambda, currItem, currIndex);
    }
}
internal class WhereStage : LinqStageNode
{
    public Value FilterLambda => SubjectCall!.Args[1];

    public WhereStage(CallInst call, LinqStageNode? source)
        : base(call, source) { }

    public override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        return Source!.EmitMoveNext(builder, currIndex);
    }
    public override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        var currItem = Source!.EmitCurrent(builder, currIndex, skipBlock);
        var predCond = builder.CreateLambdaInvoke_ItemAndIndex(FilterLambda, currItem, currIndex);
        builder.ForkAndSkipIfFalse(predCond, skipBlock);
        return currItem;
    }
}
internal class OfTypeStage : LinqStageNode
{
    public OfTypeStage(CallInst call, LinqStageNode? source)
        : base(call, source) { }

    public override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        var destType = SubjectCall!.Method.GenericParams[0];
        var currItem = Source!.EmitCurrent(builder, currIndex, skipBlock);

        if (currItem.ResultType.IsValueType) {
            //This should be rare anyway, let downstream passes will deal with it
            currItem = builder.CreateIntrinsic(CilIntrinsic.Box, currItem);
        }
        var predCond = builder.CreateIntrinsic(CilIntrinsic.AsInstance, destType, currItem);
        builder.ForkAndSkipIfFalse(predCond, skipBlock);

        return destType.IsValueType
            ? builder.CreateIntrinsic(CilIntrinsic.UnboxObj, destType, currItem)
            : currItem;
    }
    public override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        return Source!.EmitMoveNext(builder, currIndex);
    }
}
internal class CastStage : LinqStageNode
{
    public CastStage(CallInst call, LinqStageNode? source)
        : base(call, source) { }

    public override Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock)
    {
        var destType = SubjectCall!.Method.GenericParams[0];
        var currItem = Source!.EmitCurrent(builder, currIndex, skipBlock);
        return builder.CreateIntrinsic(CilIntrinsic.CastClass, destType, currItem);
    }
    public override Value EmitMoveNext(IRBuilder builder, Value currIndex)
    {
        return Source!.EmitMoveNext(builder, currIndex);
    }
}