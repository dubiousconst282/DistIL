namespace DistIL.IR;

/// <summary>
/// Represents a constant value of type `Int, Long, Double, String or null`.
/// </summary>
public abstract class Const : Value, IEquatable<Const>
{
    /// <summary> Returns a constant zero/null for the given primitive/object type. </summary>
    public static Const CreateZero(RType type)
    {
        return type.StackType switch {
            StackType.Int or StackType.Long
                            => ConstInt.Create(type, 0),
            StackType.Float => ConstFloat.Create(type, 0),
            StackType.Object => ConstNull.Create(),
            _ => throw new InvalidOperationException()
        };
    }

    public abstract bool Equals(Const? other);

    public override bool Equals(object? obj) => obj is Const c && Equals(c);
    public abstract override int GetHashCode();
}

public class ConstNull : Const
{
    private ConstNull() { }

    public static ConstNull Create() => new() { ResultType = PrimType.Object };

    public override void Print(StringBuilder sb, SlotTracker slotTracker)
    {
        sb.Append("null");
    }

    public override bool Equals(Const? other) => other is ConstNull;
    public override int GetHashCode() => 0;
}

public class ConstString : Const
{
    public string Value { get; set; } = null!;

    private ConstString() { }

    public static ConstString Create(string value) => new() { ResultType = PrimType.String, Value = value };

    public override void Print(StringBuilder sb, SlotTracker slotTracker)
    {
        sb.Append('"');
        sb.Append(Value.Replace("\"", "\\\""));
        sb.Append('"');
    }

    public override bool Equals(Const? other) => other is ConstString o && o.Value == Value;
    public override int GetHashCode() => Value.GetHashCode();
}

/// <summary>
/// A undefined value. 
/// Generally used as a phi argument for a variable that has not been 
/// assigned a value before being used.
/// </summary>
public class Undef : Value
{
    public Undef(RType type)
    {
        ResultType = type;
    }

    public override void Print(StringBuilder sb, SlotTracker slotTracker)
    {
        sb.Append($"undef({ResultType})");
    }
}