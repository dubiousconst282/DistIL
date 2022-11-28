namespace DistIL.IR;

/// <summary> Represents a local method variable. </summary>
/// <remarks> The actual value of a variable can be accessed using LoadVarInst, StoreVarInst, and VarAddrInst. </remarks>
public class Variable : TrackedValue
{
    public TypeSig Sig { get; }
    public string? Name { get; set; }

    public bool IsPinned { get; }
    /// <summary>
    /// Whether this variable's address has been exposed, or if it is alive across protected regions.
    /// Setting to true disables SSA enregistration.
    /// </summary>
    public bool IsExposed { get; set; }

    public Variable(TypeSig sig, string? name = null, bool pinned = false, bool exposed = false)
    {
        ResultType = sig.Type;
        Sig = sig;
        Name = name;
        IsPinned = pinned;
        IsExposed = exposed;
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