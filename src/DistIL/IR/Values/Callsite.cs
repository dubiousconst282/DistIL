namespace DistIL.IR;

public abstract class Callsite : Value
{
    public RType RetType { get; init; } = null!;
    /// <summary> Argument types of the method. Includes `this` if `IsInstance == true`. </summary>
    public ImmutableArray<RType> ArgTypes { get; init; }
    
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public int NumArgs => ArgTypes.Length;

    public string Name { get; init; } = null!;

    public bool IsStatic { get; init; }

    /// <summary> Whether this method is not static. </summary>
    public bool IsInstance => !IsStatic;

    /// <summary> Whether this method is pure (doesn't cause any visible side effects) </summary>
    public virtual bool IsPure => false;

    public override void Print(StringBuilder sb, SlotTracker slotTracker)
    {
        var decl = this is AsmIO.MemberDef m ? (m.DeclaringType.ToString() + "::") : "";
        sb.Append($"{(IsStatic ? "static " : "")}{RetType} {decl}{Name}(");
        sb.AppendJoin(", ", ArgTypes);
        sb.Append(')');
    }
}