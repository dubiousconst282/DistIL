namespace DistIL.IR;

/// <summary> Represents the value of a method argument, as a SSA value. </summary>
public class Argument : TrackedValue
{
    public ParamDef Param { get; private set; }
    public int Index { get; }
    public string Name => Param.Name;

    public Argument(ParamDef param, int index)
    {
        Param = param;
        Index = index;
        ResultType = param.Type;
    }

    public override void Print(PrintContext ctx)
    {
        ctx.Print("#" + Name, PrintToner.VarName);
    }
    
    /// <summary> Sets the parameter definition and <see cref="Value.ResultType"/> to be of the given type. </summary>
    public void SetResultType(TypeSig sig)
    {
        Debug.Assert(Param.Type is not GenericParamType); // updating MethodSpecs is not supported yet
        Param.Sig = sig;
        ResultType = sig.Type;
    }
}