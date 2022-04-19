namespace DistIL.IR;

//The "R" prefix doesn't mean anything in particular, but it can be thought as "Representation" or "Result".
//It's only to prevent the conflict with System.Type.
public abstract class RType : IEquatable<RType>
{
    public abstract TypeKind Kind { get; }
    public abstract StackType StackType { get; }

    /// <summary> Element type of the array, pointer or byref type. </summary>
    public virtual RType? ElemType => null;
    public virtual bool IsValueType => false;
    public virtual bool IsGeneric => false;

    public abstract string? Namespace { get; }
    public abstract string Name { get; }

    public virtual void Print(StringBuilder sb)
    {
        var ns = Namespace;
        if (ns != null) {
            sb.Append(ns);
            sb.Append(".");
        }
        sb.Append(Name);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        Print(sb);
        return sb.ToString();
    }

    /// <summary> Checks whether this type can be assigned to a variable of type `assigneeType`, assuming they are values on the evaluation stack. </summary>
    public bool IsStackAssignableTo(RType assigneeType)
    {
        var t1 = StackType;
        var t2 = assigneeType.StackType;
        if (t1 == t2) {
            return true;
        }
        //Allow implicit conversion between nint/pointer and byref
        return (t1 == StackType.NInt || t1 == StackType.ByRef) &&
               (t2 == StackType.NInt || t2 == StackType.ByRef);
    }

    public abstract bool Equals(RType? other);

    public override bool Equals(object? obj) => obj is RType o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Kind, Name);

    public static bool operator ==(RType? a, RType? b) => object.ReferenceEquals(a, b) || (a is not null && a.Equals(b));
    public static bool operator !=(RType? a, RType? b) => !(a == b);
}

public enum TypeKind
{
    Void,
    Bool,
    Char,
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    String,
    TypedRef,
    IntPtr,
    UIntPtr,
    Pointer,
    ByRef,
    Object,
    Struct,
    Array,
}
public static class TypeKindEx
{
    const byte Uns = 1 << 0; //Unsigned int
    const byte Sig = 1 << 1; //Signed int
    const byte Ptr = 1 << 2; //Pointer size
    const byte Obj = 1 << 3; //Object

    private static readonly (byte BitSize, byte Flags)[] _data = {
        (0,    0), //Void
        (8,  Uns), //Bool
        (16, Uns), //Char
        (8,  Sig), //SByte
        (8,  Uns), //Byte
        (16, Sig), //Int16
        (16, Uns), //UInt16
        (32, Sig), //Int32
        (32, Uns), //UInt32
        (64, Sig), //Int64
        (64, Uns), //UInt64
        (32,   0), //Single
        (64,   0), //Double
        (0,  Obj), //String
        (0,    0), //TypedRef
        (0,  Sig | Ptr), //IntPtr
        (0,  Uns | Ptr), //UIntPtr
        (0,  Ptr), //Pointer
        (0,  Ptr), //ByRef
        (0,  Obj), //Object
        (0,    0), //Struct
        (0,  Obj), //Array
    };

    public static int BitSize(this TypeKind type) => _data[(int)type].BitSize;
    public static bool IsSigned(this TypeKind type) => HasFlag(type, Sig);
    public static bool IsUnsigned(this TypeKind type) => HasFlag(type, Uns);
    public static bool IsPointerSize(this TypeKind type) => HasFlag(type, Ptr);

    public static bool IsInt(this TypeKind type) => HasFlag(type, Sig | Uns);
    public static bool IsFloat(this TypeKind type) => type is TypeKind.Single or TypeKind.Double;

    private static bool HasFlag(TypeKind type, byte flags)
        => (_data[(int)type].Flags & flags) != 0;

}
//I.12.1 Supported data types 
//I.12.3.2.1 The evaluation stack
public enum StackType
{
    Void,   // (no value)
    Int,    // int32
    Long,   // int64
    NInt,   // native int / unmanaged pointer
    Float,  // F
    ByRef,  // &
    Object, // O
    Struct  // value type
}