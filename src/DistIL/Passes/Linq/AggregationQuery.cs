namespace DistIL.Passes.Linq;

using DistIL.IR;
using DistIL.IR.Utils;

internal class AggregationQuery : LinqQuery
{
    public AggregationQuery(CallInst call)
        : base(call) { }

    Value? _accumulator, _seed, _isEmpty;

    public override void EmitHead(IRBuilder builder, Value? estimCount)
    {
        _seed = GetSeed(builder);
    }
    public override void EmitTail(IRBuilder builder)
    {
        if (_isEmpty != null) {
            builder.Throw(typeof(InvalidOperationException), _isEmpty);
        }
        SubjectCall.ReplaceUses(MapResult(builder, _accumulator!));
    }

    public override void EmitBody(IRBuilder builder, Value currItem, in BodyLoopData loopData)
    {
        var skipBlock = loopData.SkipBlock;
        _accumulator = loopData.CreateAccum(_seed!, emitUpdate: curr => Accumulate(builder, curr, currItem, skipBlock));

        if (_seed is Undef) {
            _isEmpty = loopData.CreateAccum(ConstInt.Create(PrimType.Bool, 1), emitUpdate: curr => ConstInt.Create(PrimType.Bool, 0));
        }
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
internal class CountQuery : AggregationQuery
{
    public CountQuery(CallInst call)
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