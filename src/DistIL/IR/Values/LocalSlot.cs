namespace DistIL.IR;

/// <summary> Represents a memory slot local to a method. </summary>
public class LocalSlot : TrackedValue
{
    public TypeDesc Type => ResultType.ElemType!;
    
    /// <summary>
    /// If this slot contains a managed object or ref, controls whether the GC should
    /// keep it at a fixed heap location for the lifetime of this slot.
    /// </summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// Whether this slot should always be considered exposed.
    /// This is currently used to control SSA promotion for slots crossing protected regions. 
    /// </summary>
    public bool IsHardExposed { get; set; }

    public MethodBody Method { get; internal set; }
    internal LocalSlot? _prev, _next;

    internal LocalSlot(MethodBody method, TypeDesc type, bool pinned = false, bool hardExposed = false)
    {
        ResultType = type.CreateByref();
        Method = method;
        IsPinned = pinned;
        IsHardExposed = hardExposed;
    }

    /// <summary> Checks if this slot is used by any instruction other than a memory load or store. </summary>
    public bool IsExposed()
    {
        if (IsHardExposed) {
            return true;
        }
        foreach (var use in Uses()) {
            if (use is not { Parent: MemoryInst, OperIndex: 0 }) {
                return true;
            }
        }
        return false;
    }

    public void Remove()
    {
        Ensure.That(NumUses == 0);
        
        if (Method != null) {
            IIntrusiveList<MethodBody, LocalSlot>.RemoveRange<MethodBody.VarLinkAccessor>(Method, this, this);
            Method = null!;
        }
    }

    public override void Print(PrintContext ctx)
    {
        ctx.Print("$" + ctx.SymTable.GetName(this), PrintToner.VarName);
    }

    public override SymbolTable? GetSymbolTable() => Method.GetSymbolTable();
}