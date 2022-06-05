namespace DistIL.Passes.Linq;

using DistIL.IR;

public abstract class Stage
{
    public Stage? Prev, Next;
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
    public static Stage? Create(CallInst call)
    {
        #pragma warning disable format
        return call.Method.Name switch {
            "Where"     => new WhereStage() { Call = call },
            "Select"    => new SelectStage() { Call = call },
            "ToArray"   => new ToArrayStage() { Call = call },
            _ => null
        };
        #pragma warning restore format
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        var slotTracker = Call.Block.Method.GetSlotTracker();

        for (var curr = this; curr != null; curr = curr.Next) {
            if (curr != this) sb.Append(" -> ");

            sb.Append(curr.GetType().Name.Replace("Stage", "("));
            int i = 0;
            foreach (var arg in curr.Call.Args) {
                if (i++ != 0) sb.Append(", ");
                arg.PrintAsOperand(sb, slotTracker);
            }
            sb.Append(")");
        }
        return sb.ToString();
    }
}
public abstract class ReductionStage : Stage
{
}


public class WhereStage : Stage
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

public class SelectStage : Stage
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
        //  int resultIndex = phi [PreHeader -> 0, Latch -> nextResultIndex]
        //  ...
        //Body:
        //  starr result, resultIndex, currItem
        //  int nextResultIndex = add resultIndex, 1
        //  ...
        //Exit:
        //  bool cond = icmp.ne resultIndex, initialLen
        //  goto cond ? Result : ActualExit
        //Resize:
        //  T[] resizedArray = new T[resultIndex]
        //  call Array.Copy(src: result, dst: resizedArray, len: resultIndex)
        //  goto ActualExit
        //ActualExit:
        //  T[] actualResult = phi [Exit -> result, Resize -> resizedResult]
        var resultArray = synther.PreHeader.CreateNewArray(synther.CurrItem.ResultType, synther.InputLen);
        var resultIndex = synther.Header.CreatePhi(PrimType.Int32);

        var body = synther.GetBody();
        body.CreateArrayStore(resultArray, resultIndex, synther.CurrItem);
        var nextResultIndex = body.CreateAdd(resultIndex, ConstInt.CreateI(1));

        resultIndex.AddArg(
            (synther.PreHeader.Block, ConstInt.CreateI(0)),
            (synther.Latch.Block, nextResultIndex)
        );

        body.SetBranch(synther.Latch.Block);

        var resizeBody = synther.GetBody(createNew: true);
        var newExit = synther.GetBody(createNew: true);

        var resizedArray = resizeBody.CreateNewArray(synther.CurrItem.ResultType, resultIndex);
        resizeBody.CreateCall(GetArrayCopyMethod(synther), resultArray, resizedArray, resultIndex);
        resizeBody.SetBranch(newExit.Block);

        var actualResult = newExit.CreatePhi(
            (synther.Exit.Block, resultArray),
            (resizeBody.Block, resizedArray)
        );
        var sizeCond = synther.Exit.CreateNe(resultIndex, synther.InputLen);
        synther.Exit.SetBranch(sizeCond, resizeBody.Block, newExit.Block);

        synther.SetResult(newExit.Block, actualResult);
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