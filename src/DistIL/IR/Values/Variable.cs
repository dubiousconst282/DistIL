namespace DistIL.IR;

/// <summary> Represents a local method variable. </summary>
/// <remarks> The actual value of a variable can be accessed using LoadVarInst, StoreVarInst, and VarAddrInst. </remarks>
public class Variable : TrackedValue
{
    public TypeDesc Type => ResultType;
    public string? Name { get; set; }
    public bool IsPinned { get; set; }
    /// <summary>
    /// Whether this variable's address has been exposed, or if it is alive across try regions.
    /// Setting to true disables SSA renaming.
    /// </summary>
    public bool IsExposed { get; set; }

    public Variable(TypeDesc type, bool isPinned = false, string? name = null)
    {
        ResultType = type;
        Name = name;
        IsPinned = isPinned;
    }

    public override void Print(PrintContext ctx)
    {
        ctx.Print("$" + (Name ?? ctx.SymTable.GetName(this)), PrintToner.VarName);
    }

    public override SymbolTable? GetSymbolTable()
    {
        var parentMethod = GetFirstUser()?.Block?.Method;
        return parentMethod?.GetSymbolTable();
    }
}