namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;

public class PropertyDef : MemberDesc, ModuleEntity
{
    public override TypeDef DeclaringType { get; }
    public ModuleDef Module => DeclaringType.Module;
    public override string Name { get; }

    public PropertyAttributes Attribs { get; }
    
    public MethodSig Sig { get; }

    public object? DefaultValue { get; }

    public MethodDef? Getter { get; }
    public MethodDef? Setter { get; }
    public IReadOnlyList<MethodDef> OtherAccessors { get; }

    private IList<CustomAttrib>? _customAttribs;

    internal PropertyDef(
        TypeDef declaryingType, string name, MethodSig sig,
        MethodDef? getter = null, MethodDef? setter = null,
        ImmutableArray<MethodDef> otherAccessors = default,
        object? defaultValue = null,
        PropertyAttributes attribs = default)
    {
        DeclaringType = declaryingType;
        Name = name;
        Sig = sig;
        Getter = getter;
        Setter = setter;
        OtherAccessors = otherAccessors.EmptyIfDefault();
        DefaultValue = defaultValue;
        Attribs = attribs;
    }

    public IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => CustomAttribExt.GetOrInitList(ref _customAttribs, readOnly);

    public override void Print(PrintContext ctx)
    {
        ctx.Print($"{DeclaringType.Name}::{Name} {{");
        if (Getter != null) ctx.Print($" get => {Getter};");
        if (Setter != null) ctx.Print($" set => {Setter};");
        ctx.Print(" }");
    }

    internal static PropertyDef Decode3(ModuleLoader loader, PropertyDefinitionHandle handle, TypeDef parent)
    {
        var info = loader._reader.GetPropertyDefinition(handle);
        var sig = new SignatureDecoder(loader, info.Signature, new GenericContext(parent)).DecodeMethodSig();
        var accs = info.GetAccessors();
        var otherAccessors = accs.Others.IsEmpty
            ? default(ImmutableArray<MethodDef>)
            : accs.Others.Select(loader.GetMethod).ToImmutableArray();

        var prop = new PropertyDef(
            parent, loader._reader.GetString(info.Name), sig,
            accs.Getter.IsNil ? null : loader.GetMethod(accs.Getter),
            accs.Setter.IsNil ? null : loader.GetMethod(accs.Setter),
            otherAccessors,
            loader._reader.DecodeConst(info.GetDefaultValue()),
            info.Attributes
        );
        prop._customAttribs = loader.DecodeCustomAttribs(info.GetCustomAttributes());
        return prop;
    }
}