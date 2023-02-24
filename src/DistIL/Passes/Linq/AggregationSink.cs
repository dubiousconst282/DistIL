namespace DistIL.Passes.Linq;

using DistIL.IR;
using DistIL.IR.Utils;

internal class AggregationSink : LinqSink
{
    public AggregationSink(CallInst call)
        : base(call) { }

    Value? _accumulator, _seed, _hasData;

    public override void EmitHead(IRBuilder builder, Value? estimCount)
    {
        _seed = GetSeed(builder);
    }
    public override void EmitTail(IRBuilder builder)
    {
        if (_hasData != null) {
            //goto hasData ? Exit : ThrowHelper
            builder.Throw(typeof(InvalidOperationException), builder.CreateEq(_hasData, ConstInt.CreateI(0)));
        }
        SubjectCall.ReplaceUses(MapResult(builder, _accumulator!));
    }

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var skipBlock = loopData.SkipBlock;

        if (_seed is not Undef) {
            _accumulator = loopData.CreateAccum(_seed!, emitUpdate: curr => Accumulate(builder, curr, currItem, skipBlock));
            return;
        }
        Debug.Assert(SubjectCall.ResultType == currItem.ResultType);

        _hasData = loopData.CreateAccum(ConstInt.Create(PrimType.Bool, 0), emitUpdate: hasData => {
            var gotData = default(Value);

            _accumulator = loopData.CreateAccum(_seed, emitUpdate: curr => {
                //nextAccum = hasData ? Accum(currItem) : currItem
                var emptyCheckBlock = builder.Block;
                var mergeBlock = builder.Method.CreateBlock(insertAfter: emptyCheckBlock).SetName("LQ_MergeAccum");
                builder.Fork(hasData, mergeBlock);

                var accumulated = Accumulate(builder, curr, currItem, skipBlock);
                var accumBlock = builder.Block;
                builder.SetBranch(mergeBlock);

                builder.SetPosition(mergeBlock);
                gotData = builder.CreatePhi(PrimType.Bool, (emptyCheckBlock, ConstInt.CreateI(1)), (accumBlock, hasData));
                return builder.CreatePhi(SubjectCall.ResultType, (emptyCheckBlock, currItem), (accumBlock, accumulated));
            });
            return gotData!;
        }).SetName("lq_has_data");
    }

    protected virtual Value GetSeed(IRBuilder builder)
    {
        if (SubjectCall.NumArgs >= 3) {
            return SubjectCall.Args[1];
        }
        return new Undef(SubjectCall.ResultType);
    }
    protected virtual Value Accumulate(IRBuilder builder, Value currAccum, Value currItem, BasicBlock skipBlock)
    {
        int lambdaIdx = SubjectCall.NumArgs >= 3 ? 2 : 1;
        return builder.CreateLambdaInvoke(SubjectCall.Args[lambdaIdx], currAccum, currItem);
    }
    protected virtual Value MapResult(IRBuilder builder, Value accum)
    {
        if (SubjectCall.NumArgs >= 4) {
            return builder.CreateLambdaInvoke(SubjectCall.Args[3], accum);
        }
        return accum;
    }
}
internal class CountSink : AggregationSink
{
    public CountSink(CallInst call)
        : base(call) { }

    protected override Value GetSeed(IRBuilder builder)
    {
        return ConstInt.CreateL(0);
    }
    protected override Value Accumulate(IRBuilder builder, Value currAccum, Value currItem, BasicBlock skipBlock)
    {
        if (SubjectCall.Args.Length >= 2) {
            var cond = builder.CreateLambdaInvoke(SubjectCall.Args[1], currItem);
            builder.Fork(cond, skipBlock);
        }
        return builder.CreateAdd(currAccum, ConstInt.CreateL(1));
    }
    protected override Value MapResult(IRBuilder builder, Value accum)
    {
        //TODO: overflow check is redundant if source is known to be a Array/List
        return builder.CreateConvert(accum, PrimType.Int32, checkOverflow: true, srcUnsigned: true);
    }
}