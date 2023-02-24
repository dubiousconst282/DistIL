namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

/*
internal class ConsumedQuery : LinqQuery
{
    public ConsumedQuery(CallInst getEnumerCall, LinqStageNode pipeline)
        : base(getEnumerCall, pipeline) { }

    public override bool Emit()
    {
        var calls = new Dictionary<string, CallInst>(4);
        var disposeCmp = default(CompareInst);

        foreach (var user in SubjectCall.Users()) {
            if (user is CallInst call) {
                calls.Add(call.Method.Name, call);
            } else if (user is CompareInst cmp) {
                disposeCmp = cmp;
            } else {
                return false;
            }
        }
        if (!calls.TryGetValue("MoveNext", out var moveNextCall) ||
            !calls.TryGetValue("get_Current", out var getCurrentCall) ||
            (!calls.TryGetValue("Dispose", out var disposeCall) && calls.Count == 3) ||
            !FindLoop(moveNextCall.Block, out var preHeaderBlock, out var oldLatchBlock)
            
        ) {
            return false;
        }
        var prevHeader = moveNextCall.Block;
        var header = new IRBuilder(prevHeader, moveNextCall.Prev);
        prevHeader.Split(moveNextCall);

        var latch = NewBlock("NewLatch", insertAfter: oldLatchBlock);
        oldLatchBlock.Last.ReplaceOperands(prevHeader, latch.Block); //old latch terminator could be a LeaveInst

        var currIndex = prevHeader.InsertPhi(PrimType.Int32).SetName("currIdx");
        var nextIndex = latch.CreateAdd(currIndex, ConstInt.CreateI(1));
        currIndex.AddArg((preHeaderBlock, ConstInt.CreateI(0)), (latch.Block, nextIndex));
        latch.SetBranch(prevHeader);

        Pipeline.EmitHead(new IRBuilder(preHeaderBlock));

        var hasNext = Pipeline.EmitMoveNext(header, currIndex);
        moveNextCall.ReplaceWith(hasNext);

        var body = new IRBuilder(getCurrentCall);
        var actualBody = body.Block.Split(getCurrentCall.Next!);
        var currItem = Pipeline.EmitCurrent(body, currIndex, latch.Block);
        body.SetBranch(actualBody);
        getCurrentCall.ReplaceWith(currItem);

        disposeCmp?.ReplaceWith(ConstInt.CreateI(0));
        disposeCall?.Remove();

        Ensure.That(SubjectCall.NumUses == 0);
        SubjectCall.Remove();
        Pipeline.DeleteSubject();
        return true;
    }

    private static bool FindLoop(BasicBlock block, out BasicBlock preHeader, out BasicBlock latch)
    {
        //Detect the existing loop using pattern matching to avoid depending on expansive dom tree/loop analysis
        preHeader = latch = null!;
        int numPreds = 0;

        foreach (var pred in block.Preds) {
            if (pred.First is GuardInst { Kind: GuardKind.Finally }) {
                preHeader = pred;
            } else if (pred.NumSuccs == 1) {
                latch = pred;
            }
            numPreds++;
        }
        return numPreds == 2 && preHeader != null && latch != null;
    }
}
*/