namespace DistIL.IR;

/// <summary>
/// Represents the value of a method argument. Differently from variables,
/// arguments are always read only, and can be used as operands in any instruction.
/// </summary>
public class Argument : TrackedValue
{
    public ParamDef Param { get; }
    
    public string? Name => Param.Name;
    public int Index => Param.Index;

    public Argument(ParamDef param)
    {
        Param = param;
        ResultType = param.Type;
    }

    public override void Print(PrintContext ctx)
    {
        ctx.Print("#" + (Name ?? Param.Index.ToString()), PrintToner.VarName);
    }
}