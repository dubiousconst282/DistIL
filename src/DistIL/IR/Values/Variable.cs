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

    public override void Print(StringBuilder sb, SlotTracker slotTracker)
    {
        sb.Append("$");
        if (Name != null) {
            sb.Append(Name);
        } else {
            sb.Append(slotTracker.GetId(this));
        }
    }

    protected override SlotTracker GetDefaultSlotTracker()
    {
        var parentMethod = GetFirstUser()?.Block.Method;
        return parentMethod?.GetSlotTracker() ?? base.GetDefaultSlotTracker();
    }
}

/// <summary>
/// Represents the value of a method argument. Differently from variables,
/// arguments are readonly (while in SSA), and can be used as operands in any instruction.
/// </summary>
public class Argument : Variable
{
    public int Index { get; }

    public Argument(TypeDesc type, int index, string? name = null)
        : base(type, false, name)
    {
        Index = index;
    }

    public override void Print(StringBuilder sb, SlotTracker slotTracker)
    {
        sb.Append("#");
        if (Name != null) {
            sb.Append(Name);
        } else {
            sb.Append($"arg{Index}");
        }
    }
}