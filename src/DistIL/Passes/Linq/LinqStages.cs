namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

internal class SelectStage : LinqStageNode
{
    public SelectStage(CallInst call, LinqStageNode drain)
        : base(call, drain) { }

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var mapLambda = SubjectCall!.Args[1];
        var mappedItem = builder.CreateLambdaInvoke_ItemAndIndex(mapLambda, currItem, loopData.CreateAccum);
        Drain.EmitBody(builder, mappedItem, loopData);
    }
}
internal class WhereStage : LinqStageNode
{
    public WhereStage(CallInst call, LinqStageNode drain)
        : base(call, drain) { }

    public override bool IsFiltering => true;

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var filterLambda = SubjectCall!.Args[1];
        var cond = builder.CreateLambdaInvoke_ItemAndIndex(filterLambda, currItem, loopData.CreateAccum);
        // if (!cond) goto SkipBlock;
        builder.Fork(cond, loopData.SkipBlock);
        Drain.EmitBody(builder, currItem, loopData);
    }
}
internal class OfTypeStage : LinqStageNode
{
    public OfTypeStage(CallInst call, LinqStageNode drain)
        : base(call, drain) { }

    public override bool IsFiltering => true;

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var destType = SubjectCall!.Method.GenericParams[0];

        if (currItem.ResultType.IsValueType) {
            currItem = builder.CreateBox(currItem.ResultType, currItem);
        }
        currItem = builder.CreateAsInstance(destType, currItem);
        builder.Fork(currItem, loopData.SkipBlock);

        if (destType.IsValueType) {
            currItem = builder.CreateUnboxObj(destType, currItem);
        }
        Drain.EmitBody(builder, currItem, loopData);
    }
}
internal class CastStage : LinqStageNode
{
    public CastStage(CallInst call, LinqStageNode drain)
        : base(call, drain) { }

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var destType = SubjectCall!.Method.GenericParams[0];

        if (currItem.ResultType.IsValueType) {
            currItem = builder.CreateBox(currItem.ResultType, currItem);
        }
        currItem = builder.CreateCastClass(destType, currItem);

        Drain.EmitBody(builder, currItem, loopData);
    }
}
internal class SkipStage : LinqStageNode
{
    public SkipStage(CallInst call, LinqStageNode sink)
        : base(call, sink) { }

    public override bool IsFiltering => true;

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        // Behavior for `count <= 0` is nop (drain all source items)
        var count = SubjectCall.Args[1];

        loopData.CreateAccum(count, emitUpdate: curr => {
            // if (count > 0) goto DecrAndSkip;
            //  ...
            // DecrAndSkip:
            //  count--;
            //  goto Skip
            var decrAndSkip = builder.Method.CreateBlock(insertAfter: loopData.SkipBlock.Prev);
            var decr = new BinaryInst(BinaryOp.Sub, curr, ConstInt.CreateI(1));
            decrAndSkip.InsertFirst(decr);
            decrAndSkip.SetBranch(loopData.SkipBlock);

            builder.Fork(builder.CreateSle(curr, ConstInt.CreateI(0)), decrAndSkip);
            Drain.EmitBody(builder, currItem, loopData);

            return decr;
        }).SetName("lq_skipRem");
    }
}
internal class TakeStage : LinqStageNode
{
    public TakeStage(CallInst call, LinqStageNode sink)
        : base(call, sink) { }

    public override bool IsFiltering => true;

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        // Behavior for `count <= 0` is to discard all elements.
        var count = SubjectCall.Args[1];

        loopData.CreateAccum(count, emitUpdate: curr => {
            // if (count <= 0) goto ExitBlock;
            // count--;
            builder.Fork(builder.CreateSgt(curr, ConstInt.CreateI(0)), loopData.Exit.Block);
            Drain.EmitBody(builder, currItem, loopData);
            return builder.CreateSub(curr, ConstInt.CreateI(1));
        }).SetName("lq_takeRem");
    }
}
internal class FlattenStage : LinqStageNode
{
    public FlattenStage(CallInst call, LinqStageNode drain)
        : base(call, drain) { }

    public override bool IsFiltering => true;
    public override bool IsGenerating => true;

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var innerLoop = new LoopBuilder(SubjectCall.Block, "LQ_Flatten_");

        var subCollection = builder.CreateLambdaInvoke(SubjectCall.Args[1], currItem);
        var enumerator = builder.CreateCallVirt("GetEnumerator", subCollection);

        var innerLoopData = loopData with {
            SkipBlock = innerLoop.Latch.Block,
            CreateAccum = (seed, emitUpdate) => 
                loopData.CreateAccum(seed, curr => innerLoop.CreateAccum(curr, emitUpdate))
        };

        innerLoop.Build(
            emitCond: header => header.CreateCallVirt("MoveNext", enumerator),
            emitBody: body => {
                var innerItem = body.CreateCallVirt("get_Current", enumerator);
                Drain.EmitBody(body, innerItem, innerLoopData);
            }
        );
        builder.SetBranch(innerLoop.PreHeader.Block);
        builder.SetPosition(innerLoop.Exit.Block);
    }
}