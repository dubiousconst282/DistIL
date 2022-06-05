namespace DistIL.Passes.Linq;

using DistIL.IR;

public class QuerySynthesizer
{
    public readonly MethodBody Method;
    public readonly Stage StartStage, EndStage;

    //Blocks for the main loop
    public readonly IRBuilder PreHeader, Header, Latch, Exit;
    public readonly PhiInst CurrIndex;
    public readonly Value InputLen, HeaderItem;

    public Value CurrItem = null!;
    public IRBuilder CurrBody;

    private Value? _result;
    private BasicBlock? _resultBlock;

    public QuerySynthesizer(MethodBody method, Stage startStage, Stage endStage)
    {
        Method = method;
        StartStage = startStage;
        EndStage = endStage;

        PreHeader = NewBlock("PreHeader");
        Header = NewBlock("Header");
        Latch = NewBlock("Latch");
        Exit = NewBlock("Exit");
        CurrBody = GetBody(true);

        //PreHeader: 
        //  int inputLen = arrlen input
        //  goto Header
        //Header: 
        //  int currIdx = phi [PreHeader -> 0, Latch -> nextIdx]
        //  T currItem = ldarr input, currIdx
        //  goto Body1
        //Latch:
        //  int nextIdx = add currIdx, 1
        //  bool cond = icmp.slt nextIdx, inputLen
        //  goto cond ? Header : Exit
        var input = startStage.GetInput();

        CurrIndex = Header.CreatePhi(PrimType.Int32);
        
        switch (input.ResultType) {
            case ArrayType:
                InputLen = PreHeader.CreateConvert(PreHeader.CreateArrayLen(input), PrimType.Int32);
                HeaderItem = Header.CreateArrayLoad(input, CurrIndex);
                break;
            default: throw new NotSupportedException();
        }
        PreHeader.SetBranch(Header.Block);
        Header.SetBranch(CurrBody.Block);

        var nextIdx = Latch.CreateAdd(CurrIndex, ConstInt.CreateI(1));
        var cond = Latch.CreateSlt(nextIdx, InputLen);
        Latch.SetBranch(cond, Header.Block, Exit.Block);

        CurrIndex.AddArg((PreHeader.Block, ConstInt.CreateI(0)), (Latch.Block, nextIdx));
    }

    private IRBuilder NewBlock(string? marker = null)
    {
        var block = Method.CreateBlock();
        var ib = new IRBuilder(block);
        if (marker != null) {
            ib.AddMarker(marker);
        }
        return ib;
    }

    public IRBuilder GetBody(bool createNew = false)
    {
        if (createNew) {
            CurrBody = NewBlock();
        }
        return CurrBody;
    }

    public void SetResult(BasicBlock exitBlock, Value value)
    {
        Assert(_result == null);
        _result = value;
        _resultBlock = exitBlock;
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

    private void Synth()
    {
        CurrItem = HeaderItem;
        for (var stage = StartStage; stage != null; stage = stage.Next) {
            CurrBody.AddMarker(stage.ToString());
            stage.Synth(this);
        }
        CurrBody.SetBranch(Exit.Block);
    }

    private void Replace()
    {
        Assert(_result != null);

        var startBlock = EndStage.Call.Block;
        var endBlock = startBlock.Split(EndStage.Call);

        startBlock.SetBranch(PreHeader.Block);
        _resultBlock!.SetBranch(endBlock);
        EndStage.Call.ReplaceWith(_result, insertIfInst: false);

        //Delete old query
        for (var stage = StartStage; stage != null; stage = stage.Next) {
            stage.Call.Remove();
        }
    }

    public static void Replace(MethodBody method, Stage startStage, Stage endStage)
    {
        var synther = new QuerySynthesizer(method, startStage, endStage);
        synther.Synth();
        synther.Replace();
    }
}