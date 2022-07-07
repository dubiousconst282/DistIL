namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;

using DistIL.IR;

/// <summary> Base class for all field entities. </summary>
public abstract class FieldDesc : MemberDesc
{
    public TypeDesc Type { get; set; } = null!;
    public abstract FieldAttributes Attribs { get; set; }

    public bool IsStatic => (Attribs & FieldAttributes.Static) != 0;
    public bool IsInstance => !IsStatic;

    public override void Print(PrintContext ctx)
    {
        Type.Print(ctx, includeNs: false);
        ctx.Print(" ");
        PrintAsOperand(ctx);
    }
    public override void PrintAsOperand(PrintContext ctx)
    {
        DeclaringType.Print(ctx, includeNs: false);
        ctx.Print("::");
        ctx.Print(Name, PrintToner.MemberName);
    }
}
public abstract class FieldDefOrSpec : FieldDesc, ModuleEntity
{
    public abstract FieldDef Definition { get; }
    public ModuleDef Module => Definition.DeclaringType.Module;

    public abstract override TypeDefOrSpec DeclaringType { get; }
}

public class FieldDef : FieldDefOrSpec
{
    public override FieldDef Definition => this;
    public override TypeDef DeclaringType { get; }
    public override FieldAttributes Attribs { get; set; }
    public override string Name { get; }

    public object? DefaultValue { get; set; }
    public bool HasDefaultValue => (Attribs & FieldAttributes.HasDefault) != 0;

    /// <summary> The field layout offset (e.g. x in [FieldOffset(x)]), or -1 if not available. </summary>
    public int LayoutOffset { get; set; }

    /// <summary> Static data associated with the field. Attribs must have HasFieldRVA, and array length must be equal to the type layout size. </summary>
    public byte[]? MappedData { get; set; }

    public FieldDef(
        TypeDef declaringType, TypeDesc type, string name, 
        FieldAttributes attribs = default, object? defaultValue = null,
        int layoutOffset = -1, byte[]? mappedData = null)
    {
        DeclaringType = declaringType;
        Type = type;
        Name = name;
        Attribs = attribs;
        DefaultValue = defaultValue;
        LayoutOffset = layoutOffset;
        MappedData = mappedData;
    }

    internal void Load(ModuleLoader loader, FieldDefinition info)
    {
        if (Attribs.HasFlag(FieldAttributes.HasFieldRVA)) {
            int rva = info.GetRelativeVirtualAddress();
            var data = loader._pe.GetSectionData(rva);
            int size = FieldDef.GetMappedDataSize(Type);
            unsafe { MappedData = new Span<byte>(data.Pointer, size).ToArray(); }
        }
        CustomAttribs = loader.DecodeCustomAttribs(info.GetCustomAttributes());
        //TODO: info.GetMarshallingDescriptor()
    }

    public static int GetMappedDataSize(TypeDesc type)
    {
        switch (type.Kind) {
            case TypeKind.Bool:
            case TypeKind.SByte:
            case TypeKind.Byte:
                return 1;
            case TypeKind.Char:
            case TypeKind.Int16:
            case TypeKind.UInt16:
                return 2;
            case TypeKind.Int32:
            case TypeKind.UInt32:
            case TypeKind.Single:
                return 4;
            case TypeKind.Int64:
            case TypeKind.UInt64:
            case TypeKind.Double:
                return 8;
            default:
                if (type is TypeDef def) {
                    return def.LayoutSize;
                }
                return 0;
        }
    }
}
public class FieldSpec : FieldDefOrSpec
{
    public override FieldDef Definition { get; }
    public override TypeSpec DeclaringType { get; }

    public override FieldAttributes Attribs {
        get => Definition.Attribs;
        set => Definition.Attribs = value;
    }
    public override string Name => Definition.Name;

    public FieldSpec(TypeSpec declaringType, FieldDef def)
    {
        DeclaringType = declaringType;
        Definition = def;
        Type = def.Type.GetSpec(new GenericContext(declaringType));
        Attribs = def.Attribs;
    }
}