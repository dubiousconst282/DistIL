namespace DistIL.Passes.Linq;

using DistIL.IR.Intrinsics;
using DistIL.IR.Utils;

internal class SelectStage : LinqStageNode
{
    public SelectStage(CallInst call, LinqStageNode sink)
        : base(call, sink) { }

    public override void EmitBody(IRBuilder builder, Value currItem, in BodyLoopData loopData)
    {
        var mapLambda = SubjectCall!.Args[1];
        var mappedItem = builder.CreateLambdaInvoke_ItemAndIndex(mapLambda, currItem, loopData.CreateAccum);
        Sink.EmitBody(builder, mappedItem, loopData);
    }
}
internal class WhereStage : LinqStageNode
{
    public WhereStage(CallInst call, LinqStageNode sink)
        : base(call, sink) { }

    public override void EmitBody(IRBuilder builder, Value currItem, in BodyLoopData loopData)
    {
        var filterLambda = SubjectCall!.Args[1];
        var predCond = builder.CreateLambdaInvoke_ItemAndIndex(filterLambda, currItem, loopData.CreateAccum);
        builder.Fork(predCond, loopData.SkipBlock);
        Sink.EmitBody(builder, currItem, loopData);
    }
}
internal class OfTypeStage : LinqStageNode
{
    public OfTypeStage(CallInst call, LinqStageNode sink)
        : base(call, sink) { }

    public override void EmitBody(IRBuilder builder, Value currItem, in BodyLoopData loopData)
    {
        var destType = SubjectCall!.Method.GenericParams[0];

        if (currItem.ResultType.IsValueType) {
            currItem = builder.CreateIntrinsic(CilIntrinsic.Box, currItem.ResultType, currItem);
        }
        currItem = builder.CreateIntrinsic(CilIntrinsic.AsInstance, destType, currItem);
        builder.Fork(currItem, loopData.SkipBlock);

        if (destType.IsValueType) {
            currItem = builder.CreateIntrinsic(CilIntrinsic.UnboxObj, destType, currItem);
        }
        Sink.EmitBody(builder, currItem, loopData);
    }
}
internal class CastStage : LinqStageNode
{
    public CastStage(CallInst call, LinqStageNode sink)
        : base(call, sink) { }

    public override void EmitBody(IRBuilder builder, Value currItem, in BodyLoopData loopData)
    {
        var destType = SubjectCall!.Method.GenericParams[0];

        if (currItem.ResultType.IsValueType) {
            currItem = builder.CreateIntrinsic(CilIntrinsic.Box, currItem.ResultType, currItem);
        }
        currItem = builder.CreateIntrinsic(CilIntrinsic.CastClass, destType, currItem);

        Sink.EmitBody(builder, currItem, loopData);
    }
}
internal class SkipStage : LinqStageNode
{
    public SkipStage(CallInst call, LinqStageNode sink)
        : base(call, sink) { }

    public override void EmitBody(IRBuilder builder, Value currItem, in BodyLoopData loopData)
    {
        var loopData_ = loopData;

        loopData_.CreateAccum(ConstInt.CreateI(0), emitUpdate: curr => {
            //goto ++curr > skipCount ? NextBody : Continue;
            var next = builder.CreateAdd(curr, ConstInt.CreateI(1));
            builder.Fork(builder.CreateSgt(next, SubjectCall.Args[1]), loopData_.SkipBlock);

            Sink.EmitBody(builder, currItem, loopData_);
            return next;
        });
    }
}
internal class FlattenStage : LinqStageNode
{
    public FlattenStage(CallInst call, LinqStageNode sink)
        : base(call, sink) { }

    public override void EmitBody(IRBuilder builder, Value currItem, in BodyLoopData loopData)
    {
        var loop = new LoopBuilder(SubjectCall.Block);

        var subCollection = builder.CreateLambdaInvoke(SubjectCall.Args[1], currItem);
        var enumerator = builder.CreateCallVirt("GetEnumerator", subCollection);

        var innerLoopData = loopData;
        innerLoopData.SkipBlock = loop.Latch.Block;

        loop.Build(
            emitCond: header => header.CreateCallVirt("MoveNext", enumerator),
            emitBody: body => {
                var currItem = body.CreateCallVirt("get_Current", enumerator);
                Sink.EmitBody(body, currItem, innerLoopData);
            }
        );
        builder.SetBranch(loop.PreHeader.Block);
        builder.SetPosition(loop.Exit.Block);
    }
}