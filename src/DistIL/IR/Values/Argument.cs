namespace DistIL.IR;

/// <summary> Represents the value of a method argument, as a SSA value. </summary>
public class Argument : TrackedValue
{
    public ParamDef Param { get; }
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
}