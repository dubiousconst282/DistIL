namespace DistIL.IR;

/// <summary>
/// Represents a constant primitive value (Int, Long, Double, String or null).
/// </summary>
public abstract class Const : Value, IEquatable<Const>
{
    /// <summary> Returns a constant zero/null for the given primitive/object type. </summary>
    /// <remarks> Note: This method returns an int32 constant for nint types. </remarks>
    public static Const CreateZero(TypeDesc type)
    {
        return type.StackType switch {
            StackType.Int or StackType.Long
                            => ConstInt.Create(type, 0),
            StackType.NInt => ConstInt.Create(PrimType.Int32, 0),
            StackType.Float => ConstFloat.Create(type, 0),
            StackType.Object => ConstNull.Create(),
            _ => throw new NotSupportedException()
        };
    }

    public abstract bool Equals(Const? other);

    public override bool Equals(object? obj) => obj is Const c && Equals(c);
    public abstract override int GetHashCode();
}

public class ConstNull : Const
{
    private ConstNull() { ResultType = PrimType.Object; }

    static readonly ConstNull _instance = new();
    public static ConstNull Create() => _instance;

    public override void Print(PrintContext ctx)
    {
        ctx.Print("null", PrintToner.Keyword);
    }

    public override bool Equals(Const? other) => other is ConstNull;
    public override int GetHashCode() => 0;
}

public class ConstString : Const
{
    public string Value { get; private init; } = null!;

    private ConstString() { }

    public static ConstString Create(string value) => new() { ResultType = PrimType.String, Value = value };

    public override void Print(PrintContext ctx)
    {
        ctx.Print($"\"{Value.Replace("\"", "\\\"")}\"", PrintToner.String);
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
    public Undef(TypeDesc type)
    {
        ResultType = type;
    }

    public override void Print(PrintContext ctx)
    {
        ctx.Print("undef", PrintToner.Keyword);
        ctx.Print($"({ResultType})");
    }
}