namespace DistIL.Passes.Linq;

using DistIL.IR;
using DistIL.IR.Utils;

internal class AggregationQuery : LinqQuery
{
    public AggregationQuery(CallInst call, LinqStageNode pipeline)
        : base(call, pipeline) { }

    public override bool Emit()
    {
        //Emit a loop like this:
        //
        //PreHeader:
        //  var seed = Seed()
        //  goto Header
        //Header:
        //  var currAccum = phi [PreHeader -> seed, Latch -> nextAccum]
        //  int currIdx = phi [PreHeader -> 0, Latch -> nextIdx]
        //  bool hasNext = Source.MoveNext(currIdx)
        //  goto hasNext ? Body : Exit
        //Body:
        //  T currItem = Source.Current()
        //  var nextAccum = AccumFn(accum, currItem)
        //  goto Latch
        //Latch:
        //  int nextIdx = add currIdx, 1
        //  goto Header
        //Exit:
        //  ...
        var preHeader = NewBlock("PreHeader");
        var header = NewBlock("Header");
        var body = NewBlock("Body");
        var latch = NewBlock("Latch");
        var exit = NewBlock("Exit");

        Pipeline.EmitHead(preHeader);
        
        var seed = GetSeed(preHeader);
        preHeader.SetBranch(header.Block);

        var currIndex = header.CreatePhi(PrimType.Int32).SetName("currIdx");
        var currAccum = header.CreatePhi(seed.ResultType).SetName("currAccum");;
        var hasNext = Pipeline.EmitMoveNext(header, currIndex);
        header.SetBranch(hasNext, body.Block, exit.Block);

        var currItem = Pipeline.EmitCurrent(body, currIndex, latch.Block);
        var itrAccum = Accumulate(body, currAccum, currItem);
        body.SetBranch(latch.Block);

        var nextIdx = latch.CreateAdd(currIndex, ConstInt.CreateI(1)).SetName("nextIdx");
        var nextAccum = itrAccum;
        if (latch.Block.NumPreds >= 2) {
            var phiArgs = latch.Block.Preds.AsEnumerable()
                    .Select(pred => new PhiArg(pred, pred == body.Block ? itrAccum : currAccum))
                    .ToArray();
            nextAccum = latch.CreatePhi(phiArgs);
        }
        currIndex.AddArg((preHeader.Block, ConstInt.CreateI(0)), (latch.Block, nextIdx));
        currAccum.AddArg((preHeader.Block, seed), (latch.Block, nextAccum));
        latch.SetBranch(header.Block);

        var newBlock = SubjectCall.Block.Split(SubjectCall, branchTo: preHeader.Block);

        var result = MapResult(exit, currAccum);
        exit.SetBranch(newBlock);

        SubjectCall.ReplaceWith(result);
        Pipeline.DeleteSubject();
        return true;
    }

    protected virtual Value GetSeed(IRBuilder builder)
    {
        if (SubjectCall.NumArgs >= 3) {
            return SubjectCall.Args[1];
        }
        //There are two obvious ways we can handle unseeded aggregates (neither are great):
        // - Duplicate MoveNext() and Current here (problematic because we will need a loop for e.g. Where() sources)
        // - Return undef() here and emit a check for `index == 0` in Accumulate()
        throw new NotImplementedException();
    }
    protected virtual Value Accumulate(IRBuilder builder, Value currAccum, Value currItem)
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
    public CountQuery(CallInst call, LinqStageNode pipeline)
        : base(call, call.NumArgs >= 2 ? new WhereStage(call, pipeline) : pipeline)
    {
        Debug.Assert(pipeline is not (ArraySource or ListSource));
    }

    protected override Value GetSeed(IRBuilder builder)
    {
        return ConstInt.CreateI(0);
    }
    protected override Value Accumulate(IRBuilder builder, Value currAccum, Value currItem)
    {
        return builder.CreateAdd(currAccum, ConstInt.CreateI(1));
    }
    protected override Value MapResult(IRBuilder builder, Value accum)
    {
        return accum;
    }
}