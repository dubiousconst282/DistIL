namespace DistIL.IR;

public class PrimType : RType
{
#pragma warning disable format
    public static readonly PrimType
        Void    = new(TypeKind.Void,   StackType.Void,     "Void",      "void"),
        Bool    = new(TypeKind.Bool,   StackType.Int,      "Boolean",   "bool"),
        Char    = new(TypeKind.Char,   StackType.Int,      "Char",      "char"),
        SByte   = new(TypeKind.SByte,  StackType.Int,      "SByte",     "sbyte"),
        Byte    = new(TypeKind.Byte,   StackType.Int,      "Byte",      "byte"),
        Int16   = new(TypeKind.Int16,  StackType.Int,      "Int16",     "short"),
        UInt16  = new(TypeKind.UInt16, StackType.Int,      "UInt16",    "ushort"),
        Int32   = new(TypeKind.Int32,  StackType.Int,      "Int32",     "int"),
        UInt32  = new(TypeKind.UInt32, StackType.Int,      "UInt32",    "uint"),
        Int64   = new(TypeKind.Int64,  StackType.Long,     "Int64",     "long"),
        UInt64  = new(TypeKind.UInt64, StackType.Long,     "UInt64",    "ulong"),
        Single  = new(TypeKind.Single, StackType.Float,    "Single",    "float"),
        Double  = new(TypeKind.Double, StackType.Float,    "Double",    "double"),
        IntPtr  = new(TypeKind.IntPtr, StackType.NInt,     "IntPtr",    "nint"),
        UIntPtr = new(TypeKind.UIntPtr,StackType.NInt,     "UIntPtr",   "nuint"),
        String  = new(TypeKind.String, StackType.Object,   "String",    "string"),
        Object  = new(TypeKind.Object, StackType.Object,   "Object",    "object");
#pragma warning restore format

    public override TypeKind Kind { get; }
    public override StackType StackType { get; }
    public override bool IsValueType => StackType is not StackType.Object;

    public override string Namespace => "System";
    public override string Name { get; }

    public string Alias { get; }

    private PrimType(TypeKind kind, StackType stackType, string name, string alias)
    {
        Kind = kind;
        StackType = stackType;
        Name = name;
        Alias = alias;
    }

    public override void Print(StringBuilder sb) => sb.Append(Alias);

    public override bool Equals(RType? other)
        => other is PrimType o && o.Kind == Kind;
}