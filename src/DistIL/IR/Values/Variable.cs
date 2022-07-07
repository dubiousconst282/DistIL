namespace DistIL.IR;

/// <summary>
/// Represents a local method variable. They should not be used directly as operands,
/// except with VarLoadInst and VarStoreInst.
/// </summary>
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

    protected override SymbolTable GetDefaultSymbolTable()
    {
        var parentMethod = GetFirstUser()?.Block?.Method;
        return parentMethod?.GetSymbolTable() ?? base.GetDefaultSymbolTable();
    }
}