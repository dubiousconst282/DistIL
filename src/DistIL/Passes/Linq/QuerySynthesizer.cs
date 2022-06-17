namespace DistIL.Passes.Linq;

using DistIL.IR;

public class QuerySynthesizer
{
    public readonly MethodBody Method;
    public readonly QueryStage StartStage, EndStage;

    //Blocks for the main loop
    public readonly IRBuilder PreHeader, Header, Latch, PreExit, Exit;
    public readonly PhiInst CurrIndex;
    public readonly Instruction InputLen, OrigItem;

    public Value CurrItem = null!;
    public IRBuilder CurrBody;

    private Value? _result;

    public QuerySynthesizer(MethodBody method, QueryStage startStage, QueryStage endStage)
    {
        Method = method;
        StartStage = startStage;
        EndStage = endStage;

        PreHeader = NewBlock("PreHeader");
        Header = NewBlock("Header");
        Latch = NewBlock("Latch");
        PreExit = NewBlock("PreExit");
        Exit = NewBlock("Exit");
        CurrBody = GetBody(true);

        //The main look looks like this:
        //PreHeader:
        //  int inputLen = arrlen input
        //  ...
        //  goto Header
        //Header:
        //  int currIdx = phi [PreHeader -> 0, Latch -> nextIdx]
        //  bool hasNext = icmp.slt currIdx
        //  goto hasNext ? Body1 : PreExit
        //Body1:
        //  T currItem = ldarr input, currIdx
        //  ...
        //BodyN:
        //  goto Latch
        //Latch:
        //  int nextIdx = add currIdx, 1
        //  goto Header
        //PreExit:
        //  ...
        //  goto Exit   added by the last reduction stage
        //Exit:
        //  T result = phi [PreExit -> ??], [PreHeader -> ??]
        var input = startStage.GetInput();

        CurrIndex = Header.CreatePhi(PrimType.Int32).SetName("currIdx");
        
        switch (input.ResultType) {
            case ArrayType:
                InputLen = PreHeader.CreateConvert(PreHeader.CreateArrayLen(input), PrimType.Int32);
                OrigItem = CurrBody.CreateArrayLoad(input, CurrIndex);
                break;
            default: throw new NotSupportedException();
        }
        InputLen.SetName("inputLen");
        OrigItem.SetName("currItem");

        PreHeader.SetBranch(Header.Block);

        var nextIdx = Latch.CreateAdd(CurrIndex, ConstInt.CreateI(1)).SetName("nextIdx");
        Latch.SetBranch(Header.Block);

        var hasNext = Header.CreateSlt(CurrIndex, InputLen).SetName("hasNext");
        Header.SetBranch(hasNext, CurrBody.Block, PreExit.Block);

        CurrIndex.AddArg((PreHeader.Block, ConstInt.CreateI(0)), (Latch.Block, nextIdx));
    }

    public void Synth()
    {
        CurrItem = OrigItem;
        for (var stage = StartStage; stage != null; stage = stage.Next) {
            CurrBody.CreateMarker(stage.ToString());
            stage.Synth(this);
        }
    }
    public void Replace()
    {
        Assert(_result != null);

        var startBlock = EndStage.Call.Block;
        var endBlock = startBlock.Split(EndStage.Call);

        startBlock.SetBranch(PreHeader.Block);
        Exit.SetBranch(endBlock);
        EndStage.Call.ReplaceWith(_result, insertIfInst: false);

        //Delete old query
        for (var stage = StartStage; stage != null; stage = stage.Next) {
            stage.Call.Remove();
        }
    }

    public IRBuilder NewBlock(string name)
    {
        var block = Method.CreateBlock().SetName("Query_" + name);
        var ib = new IRBuilder(block);
        return ib;
    }

    public IRBuilder GetBody(bool createNew = false)
    {
        if (createNew) {
            CurrBody = NewBlock("Body");
        }
        return CurrBody;
    }

    public void SetResult(Value value)
    {
        Assert(_result == null);
        _result = value;

        if (PreExit.Block.Last is not BranchInst) {
            PreExit.SetBranch(Exit.Block);
        }
    }

    //Note: we assume that lambda types are all System.Func<>
    public Value InvokeLambda(IRBuilder ib, Value lambda, params Value[] args)
    {
        var invoker = lambda.ResultType.Methods.First(m => m.Name == "Invoke");
        return ib.CreateVirtualCall(invoker, args);
    }
    public Value InvokeLambda_ItemAndIndex(IRBuilder ib, Value lambda)
    {
        var type = lambda.ResultType;
        var invoker = type.Methods.First(m => m.Name == "Invoke");

        var args = invoker.Params.Length == 3
            ? new Value[] { lambda, CurrItem, CurrIndex }
            : new Value[] { lambda, CurrItem };
        return ib.CreateVirtualCall(invoker, args);
    }

    /// <summary> Emits `if !pred.Invoke(currItem) continue; <nextBody>` and returns `nextBody`. </summary>
    public IRBuilder EmitPredTest(Value pred)
    {
        //Body:
        //  bool cond = pred(currItem)
        //  goto cond ? NextBody : Latch
        var body = GetBody();
        var nextBody = GetBody(createNew: true);
        var cond = InvokeLambda_ItemAndIndex(body, pred);
        body.SetBranch(cond, nextBody.Block, Latch.Block);
        return nextBody;
    }

    public PhiInst EmitGlobalCounter()
    {
        return EmitGlobalCounter(ConstInt.CreateI(0), (body, curr) => body.CreateAdd(curr, ConstInt.CreateI(1)));
    }
    public PhiInst EmitGlobalCounter(Value startVal, Func<IRBuilder, Value, Instruction> emitUpdate)
    {
        //Header:
        //  int count = phi [PreHeader -> 0, Latch -> nextCount]
        //  ...
        //BodyN:
        //  int nextCountImm = add count, 1
        //  goto Latch
        //Latch:
        //  int nextCount = phi [...], [BodyN -> nextCountImm]
        //  ...
        var nextCount = Latch.CreatePhi(startVal.ResultType).SetName("nextCount");
        var count = Header.CreatePhi((PreHeader.Block, startVal), (Latch.Block, nextCount)).SetName("count");

        foreach (var pred in Latch.Block.Preds) {
            nextCount.AddArg(pred, count);
        }
        var body = GetBody();
        var nextCountImm = emitUpdate(body, count).SetName("nextCountImm");
        body.SetBranch(Latch.Block);

        nextCount.AddArg(body.Block, nextCountImm);
        return count;
    }
}