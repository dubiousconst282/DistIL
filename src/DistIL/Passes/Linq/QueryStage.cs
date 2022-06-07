namespace DistIL.Passes.Linq;

using DistIL.IR;

public abstract class QueryStage
{
    public QueryStage? Prev, Next;
    public CallInst Call = null!;

    public Value GetInput()
    {
        return Call.Args[0];
    }

    public bool IsExit()
    {
        return this is ReductionStage; //TODO: or is used in a call to GetEnumerator()
    }

    public abstract void Synth(QuerySynthesizer synther);

    /// <summary> Creates a stage based on the call method name. </summary>
    public static QueryStage? Create(CallInst call)
    {
        #pragma warning disable format
        QueryStage? stage = call.Method.Name switch {
            "Where"     => new WhereStage(),
            "Select"    => new SelectStage(),
            "ToArray"   => new ToArrayStage(),
            _ => null
        };
        #pragma warning restore format

        if (stage != null) {
            stage.Call = call;
        }
        return stage;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        var slotTracker = Call.Block.Method.GetSlotTracker();

        sb.Append(GetType().Name.Replace("Stage", "("));
        int i = 0;
        foreach (var arg in Call.Args) {
            if (i++ != 0) sb.Append(", ");
            arg.PrintAsOperand(sb, slotTracker);
        }
        sb.Append(")");
        return sb.ToString();
    }
}
public abstract class ReductionStage : QueryStage
{
}


public class WhereStage : QueryStage
{
    public override void Synth(QuerySynthesizer synther)
    {
        //Body:
        //  bool cond = predicate(currItem)
        //  goto cond ? NextBody : Latch
        var body = synther.GetBody();
        var nextBody = synther.GetBody(createNew: true);
        var cond = synther.InvokeLambda_ItemAndIndex(body, Call.Args[1]);
        body.SetBranch(cond, nextBody.Block, synther.Latch.Block);
    }
}

public class SelectStage : QueryStage
{
    public override void Synth(QuerySynthesizer synther)
    {
        //Body:
        //  T nextItem = transform(currItem)
        var block = synther.GetBody();
        synther.CurrItem = synther.InvokeLambda_ItemAndIndex(block, Call.Args[1]);
    }
}

public class ToArrayStage : ReductionStage
{
    public override void Synth(QuerySynthesizer synther)
    {
        //PreHeader:
        //  T[] result = new T[initialLen]
        //  ...
        //Header:
        //  int resIdx = phi [PreHeader -> 0, Latch -> nextResIdx]
        //  ...
        //BodyN:
        //  starr result, resIdx, currItem
        //  int nextResIdxImm = add resIdx, 1
        //  goto Latch
        //Latch:
        //  int nextResIdx = phi [...], [BodyN -> nextResIdxImm]
        //  ...
        //PreExit:
        //  bool cond = icmp.ne resIdx, initialLen
        //  goto cond ? Resize : Exit
        //Resize:
        //  T[] resizedArray = new T[resIdx]
        //  call Array.Copy(src: result, dst: resizedArray, len: resIdx)
        //  goto Exit
        //Exit:
        //  T[] actualResult = phi [Exit -> result, Resize -> resizedResult]
        var resultArray = synther.PreHeader.CreateNewArray(synther.CurrItem.ResultType, synther.InputLen).SetName("resultArr");

        var latch = synther.Latch;
        var nextResIdxPhi = latch.CreatePhi(PrimType.Int32).SetName("nextResIdx");
        var resIdx = synther.Header.CreatePhi(
            (synther.PreHeader.Block, ConstInt.CreateI(0)),
            (latch.Block, nextResIdxPhi)
        ).SetName("resIdx");

        foreach (var pred in latch.Block.Preds) {
            nextResIdxPhi.AddArg(pred, resIdx);
        }
        var body = synther.GetBody();
        body.CreateArrayStore(resultArray, resIdx, synther.CurrItem);
        var nextResIdxImm = body.CreateAdd(resIdx, ConstInt.CreateI(1)).SetName("nextResIdxImm");
        body.SetBranch(latch.Block);
        nextResIdxPhi.AddArg(body.Block, nextResIdxImm);

        var preExit = synther.PreExit;
        var exit = synther.Exit;
        var resizeBody = synther.NewBlock("Resize");

        var resizedArray = resizeBody.CreateNewArray(synther.CurrItem.ResultType, resIdx);
        resizeBody.CreateCall(GetArrayCopyMethod(synther), resultArray, resizedArray, resIdx);
        resizeBody.SetBranch(exit.Block);

        var sizeMatches = preExit.CreateNe(resIdx, synther.InputLen);
        preExit.SetBranch(sizeMatches, resizeBody.Block, exit.Block);

        var actualResult = exit.CreatePhi(
            (preExit.Block, resultArray),
            (resizeBody.Block, resizedArray)
        );
        synther.SetResult(actualResult);
    }

    private MethodDesc GetArrayCopyMethod(QuerySynthesizer synther)
    {
        var mod = synther.Method.Definition.Module;
        var t_Array = mod.Import(typeof(Array));
        var copyMethod = t_Array.FindMethod("Copy", new MethodSig(PrimType.Void, t_Array, t_Array, PrimType.Int32));
        Ensure(copyMethod != null, "Missing Array.Copy() method");
        return copyMethod;
    }
}