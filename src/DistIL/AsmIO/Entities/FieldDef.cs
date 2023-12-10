namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;

/// <summary> Base class for all field entities. </summary>
public abstract class FieldDesc : MemberDesc
{
    public TypeSig Sig { get; set; } = null!;
    public TypeDesc Type => Sig.Type;
    public abstract FieldAttributes Attribs { get; set; }

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

    /// <summary> Binds generic type parameters using the given context. </summary>
    /// <remarks>
    /// If the field's declaring type is not generic, or if the context is empty, 
    /// the current instance may be returned unchanged.
    /// </remarks>
    public abstract FieldDesc GetSpec(GenericContext ctx);
}
public abstract class FieldDefOrSpec : FieldDesc, ModuleEntity
{
    public abstract FieldDef Definition { get; }
    public ModuleDef Module => Definition.DeclaringType.Module;

    public abstract override TypeDefOrSpec DeclaringType { get; }

    public override FieldDesc GetSpec(GenericContext ctx)
    {
        if (!DeclaringType.IsGeneric) return this;
        
        var newParent = DeclaringType.GetSpec(ctx);
        return newParent != DeclaringType 
            ? ((TypeSpec)newParent).GetMapping(Definition) 
            : this;
    }

    public virtual IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => Definition.GetCustomAttribs(readOnly);
}

public class FieldDef : FieldDefOrSpec
{
    public override FieldDef Definition => this;
    public override TypeDef DeclaringType { get; }
    public override string Name { get; set; }

    public override FieldAttributes Attribs { get; set; }

    public object? DefaultValue { get; set; }
    public bool HasDefaultValue => (Attribs & FieldAttributes.HasDefault) != 0;

    /// <summary> The field layout offset (e.g. x in [FieldOffset(x)]), or -1 if not available. </summary>
    public int LayoutOffset { get; set; }
    public bool HasLayoutOffset => (DeclaringType.Attribs & TypeAttributes.ExplicitLayout) != 0;

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

    public override IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => CustomAttribUtils.GetOrInitList(ref _customAttribs, readOnly);

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

    public override FieldAttributes Attribs {
        get => Definition.Attribs;
        set => throw new InvalidOperationException();
    }
    public override string Name {
        get => Definition.Name;
        set => throw new InvalidOperationException();
    }

    internal FieldSpec(TypeSpec declaringType, FieldDef def)
    {
        DeclaringType = declaringType;
        Definition = def;
        Sig = def.Sig.GetSpec(new GenericContext(declaringType));
    }
}