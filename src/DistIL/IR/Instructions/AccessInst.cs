namespace DistIL.IR;

/// <summary> Marker for an instruction that accesses a variable, pointer, array, or field. </summary>
public interface AccessInst
{
    Value Location { get; }
    TypeDesc LocationType { get; }
}
/// <summary> Marker for an instruction that reads a variable, pointer, array, or field. </summary>
public interface LoadInst : AccessInst
{
}
/// <summary> Marker for an instruction that writes to a variable, pointer, array, or field. </summary>
public interface StoreInst : AccessInst
{
    Value Value { get; }
    bool IsCoerced => MustBeCoerced(LocationType, Value);

    /// <summary>
    /// Creates a sequence of instructions that truncates or rounds the value to what it would have been after
    /// a store/load roundtrip to a location of type <paramref name="destType"/>, or returns <paramref name="val"/> itself if no truncation would occur.
    /// </summary>
    public static Value Coerce(TypeDesc destType, Value val, Instruction insertBefore)
    {
        if (!MustBeCoerced(destType, val)) {
            return val;
        }
        var conv = new ConvertInst(val, destType);
        conv.InsertBefore(insertBefore);
        return conv;
    }
    public static bool MustBeCoerced(TypeDesc destType, Value srcValue)
    {
        if (destType.Kind.IsSmallInt() && srcValue is ConstInt cons) {
            return !cons.FitsInType(destType);
        }
        return MustBeCoerced(destType, srcValue.ResultType);
    }
    public static bool MustBeCoerced(TypeDesc destType, TypeDesc srcType)
    {
        //III.1.6 Implicit argument coercion

        //`NInt -> &` as in "Start GC Tracking" sounds particularly brittle. Not even Roslyn makes guarantees about it:
        //  https://github.com/dotnet/runtime/issues/34501#issuecomment-608548207
        //It's probably for the best if we don't support it.
        return
            (destType.Kind.IsSmallInt() && !srcType.Kind.IsSmallInt()) ||
            (destType.StackType == StackType.Int && srcType.StackType == StackType.NInt) ||
            (destType.Kind == TypeKind.Single && srcType.Kind == TypeKind.Double);
    }
}