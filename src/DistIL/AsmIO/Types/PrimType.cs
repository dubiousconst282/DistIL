namespace DistIL.AsmIO;

using DistIL.IR;

/// <summary> Represents an internal reference to a known primitive or system type. </summary>
public class PrimType : TypeDesc
{
    static readonly Dictionary<string, PrimType> _fromAlias = new();

#pragma warning disable format
    public static readonly PrimType
        Void    = new(TypeKind.Void,    StackType.Void,     "Void",      "void"),
        Bool    = new(TypeKind.Bool,    StackType.Int,      "Boolean",   "bool"),
        Char    = new(TypeKind.Char,    StackType.Int,      "Char",      "char"),
        SByte   = new(TypeKind.SByte,   StackType.Int,      "SByte",     "sbyte"),
        Byte    = new(TypeKind.Byte,    StackType.Int,      "Byte",      "byte"),
        Int16   = new(TypeKind.Int16,   StackType.Int,      "Int16",     "short"),
        UInt16  = new(TypeKind.UInt16,  StackType.Int,      "UInt16",    "ushort"),
        Int32   = new(TypeKind.Int32,   StackType.Int,      "Int32",     "int"),
        UInt32  = new(TypeKind.UInt32,  StackType.Int,      "UInt32",    "uint"),
        Int64   = new(TypeKind.Int64,   StackType.Long,     "Int64",     "long"),
        UInt64  = new(TypeKind.UInt64,  StackType.Long,     "UInt64",    "ulong"),
        Single  = new(TypeKind.Single,  StackType.Float,    "Single",    "float"),
        Double  = new(TypeKind.Double,  StackType.Float,    "Double",    "double"),
        IntPtr  = new(TypeKind.IntPtr,  StackType.NInt,     "IntPtr",    "nint"),
        UIntPtr = new(TypeKind.UIntPtr, StackType.NInt,     "UIntPtr",   "nuint"),
        String  = new(TypeKind.String,  StackType.Object,   "String",    "string"),
        Object  = new(TypeKind.Object,  StackType.Object,   "Object",    "object"),
        Array   = new(TypeKind.Array,   StackType.Object,   "Array",     null),
        ValueType=new(TypeKind.Struct,  StackType.Struct,   "ValueType", null),
        TypedRef= new(TypeKind.TypedRef, StackType.Struct,  "TypedReference", null);
#pragma warning restore format

    public override TypeKind Kind { get; }
    public override StackType StackType { get; }
    public override TypeDesc? BaseType => null;

    public override string Namespace => "System";
    public override string Name { get; }

    public override bool IsValueType => StackType != StackType.Object;

    public string? Alias { get; }

    private PrimType(TypeKind kind, StackType stackType, string name, string? alias)
    {
        Kind = kind;
        StackType = stackType;
        Name = name;
        Alias = alias;

        if (alias != null) {
            _fromAlias[alias] = this;
        }
    }

    public static PrimType? GetFromAlias(string alias) => _fromAlias.GetValueOrDefault(alias);

    public TypeDef GetDefinition(ModuleDef module) => module.SysTypes.GetPrimitiveDef(Kind);

    public override void Print(PrintContext ctx, bool includeNs = true)
    {
        if (Alias != null) {
            ctx.Print(Alias, PrintToner.Keyword);
        } else {
            base.Print(ctx, includeNs);
        }
    }

    public override bool Equals(TypeDesc? other)
        => other is PrimType o && o.Kind == Kind;
}