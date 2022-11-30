namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;

/// <summary> Base class for all field entities. </summary>
public abstract class FieldDesc : MemberDesc
{
    public TypeSig Sig { get; set; } = null!;
    public TypeDesc Type => Sig.Type;
    public abstract FieldAttributes Attribs { get; }

    public bool IsStatic => (Attribs & FieldAttributes.Static) != 0;
    public bool IsInstance => !IsStatic;

    public override void Print(PrintContext ctx)
    {
        Sig.Print(ctx);
        ctx.Print(" ");
        PrintAsOperand(ctx);
    }
    public override void PrintAsOperand(PrintContext ctx)
    {
        DeclaringType.Print(ctx);
        ctx.Print("::");
        ctx.Print(Name, PrintToner.MemberName);
    }

    public abstract FieldDesc GetSpec(GenericContext ctx);
}
public abstract class FieldDefOrSpec : FieldDesc, ModuleEntity
{
    public abstract FieldDef Definition { get; }
    public ModuleDef Module => Definition.DeclaringType.Module;

    public abstract override TypeDefOrSpec DeclaringType { get; }

    public override FieldDesc GetSpec(GenericContext ctx)
    {
        var newDeclType = DeclaringType.GetSpec(ctx);
        return newDeclType != DeclaringType
            ? new FieldSpec((TypeSpec)newDeclType, Definition)
            : this;
    }
}

public class FieldDef : FieldDefOrSpec
{
    public override FieldDef Definition => this;
    public override TypeDef DeclaringType { get; }
    public override string Name { get; }

    public override FieldAttributes Attribs { get; }

    public object? DefaultValue { get; set; }
    public bool HasDefaultValue => (Attribs & FieldAttributes.HasDefault) != 0;

    /// <summary> The field layout offset (e.g. x in [FieldOffset(x)]), or -1 if not available. </summary>
    public int LayoutOffset { get; set; }

    /// <summary> Static data associated with the field. Attribs must have HasFieldRVA, and array length must be equal to the type layout size. </summary>
    public byte[]? MappedData { get; set; }

    public byte[]? MarshallingDesc { get; set; }

    internal IList<CustomAttrib>? _customAttribs;

    public FieldDef(
        TypeDef declaringType, TypeSig sig, string name, 
        FieldAttributes attribs = default, object? defaultValue = null,
        int layoutOffset = -1, byte[]? mappedData = null)
    {
        DeclaringType = declaringType;
        Sig = sig;
        Name = name;
        Attribs = attribs;
        DefaultValue = defaultValue;
        LayoutOffset = layoutOffset;
        MappedData = mappedData;
    }

    public IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => CustomAttribExt.GetOrInitList(ref _customAttribs, readOnly);

    internal static FieldDef Decode(ModuleLoader loader, FieldDefinition info)
    {
        var declaringType = loader.GetType(info.GetDeclaringType());

        var sigDecoder = new SignatureDecoder(loader, info.Signature, new GenericContext(declaringType));
        sigDecoder.ExpectHeader(SignatureKind.Field);

        var type = sigDecoder.DecodeTypeSig();

        return new FieldDef(
            declaringType, type, loader._reader.GetString(info.Name),
            info.Attributes,
            loader._reader.DecodeConst(info.GetDefaultValue()),
            info.GetOffset()
        );
    }
    internal void Load3(ModuleLoader loader, FieldDefinition info)
    {
        if (Attribs.HasFlag(FieldAttributes.HasFieldRVA)) {
            int rva = info.GetRelativeVirtualAddress();
            var data = loader._pe.GetSectionData(rva);
            int size = GetMappedDataSize(Type);
            unsafe { MappedData = new Span<byte>(data.Pointer, size).ToArray(); }
        }
        if (Attribs.HasFlag(FieldAttributes.HasFieldMarshal)) {
            MarshallingDesc = loader._reader.GetBlobBytes(info.GetMarshallingDescriptor());
        }
        _customAttribs = loader.DecodeCustomAttribs(info.GetCustomAttributes());
    }

    public static int GetMappedDataSize(TypeDesc type)
    {
        return type is TypeDef def 
            ? def.LayoutSize
            : type.Kind.Size();
    }
}
public class FieldSpec : FieldDefOrSpec
{
    public override FieldDef Definition { get; }
    public override TypeSpec DeclaringType { get; }

    public override FieldAttributes Attribs => Definition.Attribs;
    public override string Name => Definition.Name;

    internal FieldSpec(TypeSpec declaringType, FieldDef def)
    {
        DeclaringType = declaringType;
        Definition = def;
        Sig = def.Sig.GetSpec(new GenericContext(declaringType));
    }
}