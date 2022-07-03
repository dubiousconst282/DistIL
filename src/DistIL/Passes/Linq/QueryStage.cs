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
            "Count"     => new CountStage(),
            _ => null
        };
        #pragma warning restore format

        if (stage != null) {
            stage.Call = call;
        }
        return stage;
    }

    public override string ToString() => GetType().Name.Replace("Stage", "");
}
public abstract class ReductionStage : QueryStage
{
}


public class WhereStage : QueryStage
{
    public override void Synth(QuerySynthesizer synther)
    {
        synther.EmitPredTest(Call.Args[1]);
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

        var resIdx = synther.EmitGlobalCounter(ConstInt.CreateI(0), (body, currIdx) => {
            body.CreateArrayStore(resultArray, currIdx, synther.CurrItem);
            return body.CreateAdd(currIdx, ConstInt.CreateI(1));
        });
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

public class CountStage : ReductionStage
{
    public override void Synth(QuerySynthesizer synther)
    {
        if (Call.Args is [_, var predicate]) {
            synther.EmitPredTest(predicate);
        }
        synther.SetResult(synther.EmitGlobalCounter());
    }
}

public class SumStage : ReductionStage
{
    public override void Synth(QuerySynthesizer synther)
    {
        var type = Call.ResultType;
        var startVal = default(Value);

        if (type.Name == "Decimal") {
            var field = type.FindField("Zero") ?? throw new NotImplementedException();
            startVal = synther.PreHeader.CreateFieldLoad(field);
        } else {
            startVal = Const.CreateZero(type);
        }

        var inc = synther.CurrItem;
        if (Call.Args is [_, var mapper]) {
            //inc = synther.InvokeLambda(body, mapper, )
        }

        var sum = synther.EmitGlobalCounter(startVal, (body, currSum) => {
            if (type.StackType is StackType.Float) {
                return body.CreateBin(BinaryOp.FAdd, currSum, inc);
            } else if (type.StackType is StackType.Int or StackType.Long) {
                return body.CreateBin(BinaryOp.AddOvf, currSum, inc);
            } else {
                throw new NotImplementedException();
            }
        });
        synther.SetResult(sum);
    }
}