namespace DistIL.Passes.Linq;

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
