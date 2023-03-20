namespace DistIL.IR;

/// <summary> Represents a method local memory slot (variable). </summary>
public class LocalSlot : TrackedValue
{
    public TypeDesc Type => ResultType.ElemType!;
    
    public string? Name { get; }
    public bool IsPinned { get; }

    /// <summary>
    /// Whether this slot should always be considered exposed.
    /// This is currently used to control SSA promotion for slots crossing protected regions. 
    /// </summary>
    public bool HardExposed { get; set; }

    public LocalSlot(TypeDesc type, string? name = null, bool pinned = false, bool hardExposed = false)
    {
        ResultType = type.CreateByref();
        Name = name;
        IsPinned = pinned;
        HardExposed = hardExposed;
    }

    /// <summary> Checks if this slot is used by any instruction other than a memory load or store. </summary>
    public bool IsExposed()
    {
        if (HardExposed) {
            return true;
        }
        foreach (var use in Uses()) {
            if (use is not { Parent: MemoryInst, OperIndex: 0 }) {
                return true;
            }
        }
        return false;
    }

    public override void Print(PrintContext ctx)
    {
        ctx.Print("$" + (Name ?? ctx.SymTable.GetName(this)), PrintToner.VarName);
    }

    public override SymbolTable? GetSymbolTable()
    {
        var parentMethod = Users().FirstOrDefault()?.Block?.Method;
        return parentMethod?.GetSymbolTable();
    }
}