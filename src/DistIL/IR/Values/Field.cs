namespace DistIL.IR;

using System.Text;

public abstract class Field : Value
{
    public abstract RType DeclaringType { get; }
    public RType Type { get; init; } = null!;
    public string Name { get; init; } = null!;

    public abstract bool IsStatic { get; }

    public bool IsInstance => !IsStatic;

    public override void Print(StringBuilder sb, SlotTracker slotTracker)
    {
        sb.Append($"{(IsStatic ? "static " : "")}{Type} {DeclaringType}::{Name}");
    }
}