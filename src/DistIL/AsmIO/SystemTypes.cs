namespace DistIL.AsmIO;

public class SystemTypes
{
    public TypeDef Void { get; }
    public TypeDef Bool { get; }
    public TypeDef Char { get; }
    public TypeDef SByte { get; }
    public TypeDef Byte { get; }
    public TypeDef Int16 { get; }
    public TypeDef UInt16 { get; }
    public TypeDef Int32 { get; }
    public TypeDef UInt32 { get; }
    public TypeDef Int64 { get; }
    public TypeDef UInt64 { get; }
    public TypeDef Single { get; }
    public TypeDef Double { get; }
    public TypeDef IntPtr { get; }
    public TypeDef UIntPtr { get; }
    public TypeDef String { get; }
    public TypeDef Object { get; }

    public TypeDef ValueType { get; }
    public TypeDef Enum { get; }

    public TypeDef Array { get; }
    public TypeDef TypedRef { get; }
    public TypeDef Type { get; }

    public TypeDef RuntimeTypeHandle { get; }
    public TypeDef RuntimeFieldHandle { get; }
    public TypeDef RuntimeMethodHandle { get; }

    internal SystemTypes(ModuleDef coreMod)
    {
        TypeDef Get(string name) => coreMod.FindType("System", name, throwIfNotFound: true);

        Void = Get("Void");
        Bool = Get("Boolean");
        Char = Get("Char");
        SByte = Get("SByte"); 
        Byte = Get("Byte");
        Int16 = Get("Int16"); 
        UInt16 = Get("UInt16");
        Int32 = Get("Int32"); 
        UInt32 = Get("UInt32");
        Int64 = Get("Int64"); 
        UInt64 = Get("UInt64");
        Single = Get("Single");
        Double = Get("Double");
        IntPtr = Get("IntPtr");
        UIntPtr = Get("UIntPtr");
        String = Get("String");
        Object = Get("Object");

        ValueType = Get("ValueType");
        Enum = Get("Enum");
        Type = Get("Type");
        Array = Get("Array");
        TypedRef = Get("TypedReference");

        RuntimeTypeHandle = Get("RuntimeTypeHandle");
        RuntimeFieldHandle = Get("RuntimeFieldHandle");
        RuntimeMethodHandle = Get("RuntimeMethodHandle");
    }

    public TypeDef GetPrimitiveDef(TypeKind kind)
    {
        return kind switch {
            TypeKind.Void   => Void,
            TypeKind.Bool   => Bool,
            TypeKind.Char   => Char,
            TypeKind.SByte  => SByte,
            TypeKind.Byte   => Byte,
            TypeKind.Int16  => Int16,
            TypeKind.UInt16 => UInt16,
            TypeKind.Int32  => Int32,
            TypeKind.UInt32 => UInt32,
            TypeKind.Int64  => Int64,
            TypeKind.UInt64 => UInt64,
            TypeKind.Single => Single,
            TypeKind.Double => Double,
            TypeKind.IntPtr => IntPtr,
            TypeKind.UIntPtr=> UIntPtr,
            TypeKind.TypedRef => TypedRef,
            TypeKind.String => String,
            TypeKind.Object => Object,
            TypeKind.Array  => Array,
            _ => throw new ArgumentException()
        };
    }
}