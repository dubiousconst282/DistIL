namespace DistIL.AsmIO;

using System.Reflection.Metadata;

/// <summary> Represents a reference to a known primitive or system type. </summary>
public class PrimType : TypeDesc
{
    static readonly Dictionary<(string Name, bool IsAlias), PrimType> _fromName = new();

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
        ValueType=new(TypeKind.Object,  StackType.Object,   "ValueType", null),
        TypedRef= new(TypeKind.TypedRef, StackType.Struct,  "TypedReference", null);
#pragma warning restore format

    public override TypeKind Kind { get; }
    public override StackType StackType { get; }
    public override TypeDesc? BaseType => StackType switch {
        < StackType.Object => ValueType,
        StackType.Struct => Object,
        StackType.Object => this == Object ? null : Object
    };

    public override string Namespace => "System";
    public override string Name { get; }
    public string? Alias { get; }

    public override bool IsValueType => StackType != StackType.Object;

    public override IReadOnlyList<MethodDesc> Methods => throw new InvalidOperationException();
    public override IReadOnlyList<FieldDesc> Fields => throw new InvalidOperationException();

    private PrimType(TypeKind kind, StackType stackType, string name, string? alias)
    {
        Kind = kind;
        StackType = stackType;
        Name = name;
        Alias = alias;

        if (alias != null) {
            _fromName.Add((alias, true), this);
        }
        _fromName.Add((name, false), this);
    }

    public static PrimType? GetFromAlias(string alias) => _fromName.GetValueOrDefault((alias, true));

    public static PrimType? GetFromDefinition(TypeDef def)
        => IsSystemType(def) ? _fromName.GetValueOrDefault((def.Name, false)) : null;

    public static PrimType GetFromKind(TypeKind kind) => GetFromSrmCode(kind.ToSrmTypeCode());

    private static bool IsSystemType(TypeDef type)
        => type.Namespace == "System" && type.IsCorelibType();

    internal static PrimType GetFromSrmCode(PrimitiveTypeCode typeCode)
    {
        return typeCode switch {
            PrimitiveTypeCode.Void    => Void,
            PrimitiveTypeCode.Boolean => Bool,
            PrimitiveTypeCode.Char    => Char,
            PrimitiveTypeCode.SByte   => SByte,
            PrimitiveTypeCode.Byte    => Byte,
            PrimitiveTypeCode.Int16   => Int16,
            PrimitiveTypeCode.UInt16  => UInt16,
            PrimitiveTypeCode.Int32   => Int32,
            PrimitiveTypeCode.UInt32  => UInt32,
            PrimitiveTypeCode.Int64   => Int64,
            PrimitiveTypeCode.UInt64  => UInt64,
            PrimitiveTypeCode.Single  => Single,
            PrimitiveTypeCode.Double  => Double,
            PrimitiveTypeCode.IntPtr  => IntPtr,
            PrimitiveTypeCode.UIntPtr => UIntPtr,
            PrimitiveTypeCode.String  => String,
            PrimitiveTypeCode.Object  => Object,
            PrimitiveTypeCode.TypedReference => TypedRef,
            _ => throw new NotSupportedException()
        };
    }

    public TypeDef GetDefinition(ModuleResolver resolver) => resolver.SysTypes.GetPrimitiveDef(Kind);
    public bool IsDefinition(TypeDef def) => def.Name == Name && IsSystemType(def);

    public override void Print(PrintContext ctx, bool includeNs = false)
    {
        if (Alias != null && !includeNs) {
            ctx.Print(Alias, PrintToner.Keyword);
        } else {
            base.Print(ctx, includeNs);
        }
    }

    public override bool Equals(TypeDesc? other)
        => (other is PrimType o && o.Kind == Kind) ||
           (other is TypeDef d && IsDefinition(d));
}