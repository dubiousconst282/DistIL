namespace DistIL.AsmIO;

using System.Reflection.Metadata;

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

    public static StackType ToStackType(this TypeKind type)
        => type switch {
            TypeKind.Void => StackType.Void,
            >= TypeKind.Bool and <= TypeKind.UInt32 => StackType.Int,
            TypeKind.Int64 or TypeKind.UInt64 => StackType.Long,
            TypeKind.Single or TypeKind.Double => StackType.Float,
            TypeKind.IntPtr or TypeKind.UIntPtr or TypeKind.IntPtr => StackType.NInt,
            TypeKind.ByRef => StackType.ByRef,
            TypeKind.Struct or TypeKind.TypedRef => StackType.Struct,
            _ => StackType.Object,
        };

    internal static PrimitiveTypeCode ToSrmTypeCode(this TypeKind kind)
    {
        return kind switch {
            TypeKind.Void   => PrimitiveTypeCode.Void,
            TypeKind.Bool   => PrimitiveTypeCode.Boolean,
            TypeKind.Char   => PrimitiveTypeCode.Char,
            TypeKind.SByte  => PrimitiveTypeCode.SByte,
            TypeKind.Byte   => PrimitiveTypeCode.Byte,
            TypeKind.Int16  => PrimitiveTypeCode.Int16,
            TypeKind.UInt16 => PrimitiveTypeCode.UInt16,
            TypeKind.Int32  => PrimitiveTypeCode.Int32,
            TypeKind.UInt32 => PrimitiveTypeCode.UInt32,
            TypeKind.Int64  => PrimitiveTypeCode.Int64,
            TypeKind.UInt64 => PrimitiveTypeCode.UInt64,
            TypeKind.Single => PrimitiveTypeCode.Single,
            TypeKind.Double => PrimitiveTypeCode.Double,
            TypeKind.IntPtr => PrimitiveTypeCode.IntPtr,
            TypeKind.UIntPtr => PrimitiveTypeCode.UIntPtr,
            TypeKind.String => PrimitiveTypeCode.String,
            TypeKind.Object => PrimitiveTypeCode.Object,
            TypeKind.TypedRef => PrimitiveTypeCode.TypedReference,
            _ => throw new NotSupportedException()
        };
    }

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