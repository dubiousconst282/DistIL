namespace DistIL.IR;

/// <summary> Marker for an instruction that accesses a variable, pointer, array, or field. </summary>
public interface AccessInst
{
    Value Location { get; }
}
/// <summary> Marker for an instruction that reads a variable, pointer, array, or field. </summary>
public interface LoadInst : AccessInst
{
}
/// <summary> Marker for an instruction that writes to a variable, pointer, array, or field. </summary>
public interface StoreInst : AccessInst
{
    Value Value { get; }

    /// <summary>
    /// Creates a sequence of instructions that truncates or rounds the value to what it would have been after
    /// a store/load roundtrip to a location of type `destType`, or returns `val` itself if no truncation would occur.
    /// See <see cref="IsCoerced(TypeDesc, TypeDesc)"/>.
    /// </summary>
    public static Value CreateCoercedValue(TypeDesc destType, Value val, Instruction insertBefore)
    {
        return val;
    }
    public static bool IsCoerced(TypeDesc destType, TypeDesc srcType)
    {
        //III.1.6 Implicit argument coercion

        return false; //TODO
    }
}