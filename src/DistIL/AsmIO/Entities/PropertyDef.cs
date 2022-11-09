namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;

public class PropertyDef : MemberDesc, ModuleEntity
{
    public override TypeDef DeclaringType { get; }
    public ModuleDef Module => DeclaringType.Module;

    public PropertyAttributes Attribs { get; init; }
    public override string Name { get; }
    
    public TypeDesc Type { get; }
    public IReadOnlyList<TypeDesc> ParamTypes { get; }
    public bool IsInstance { get; }

    public object? DefaultValue { get; }

    public MethodDef? Getter { get; }
    public MethodDef? Setter { get; }
    public IReadOnlyList<MethodDef> OtherAccessors { get; }

    public PropertyDef(
        TypeDef declaryingType, string name,
        TypeDesc type, ImmutableArray<TypeDesc> paramTypes = default, bool? isInstance = null,
        MethodDef? getter = null, MethodDef? setter = null,
        ImmutableArray<MethodDef> otherAccessors = default,
        object? defaultValue = null,
        PropertyAttributes attribs = default)
    {
        DeclaringType = declaryingType;
        Name = name;
        Type = type;
        ParamTypes = paramTypes.EmptyIfDefault();
        IsInstance = isInstance ?? (getter ?? setter)!.IsInstance;
        Getter = getter;
        Setter = setter;
        OtherAccessors = otherAccessors.EmptyIfDefault();
        DefaultValue = defaultValue;
        Attribs = attribs;
    }

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
        var sig = info.DecodeSignature(loader._typeProvider, default);
        var accs = info.GetAccessors();
        var otherAccessors = accs.Others.IsEmpty
            ? default(ImmutableArray<MethodDef>)
            : accs.Others.Select(loader.GetMethod).ToImmutableArray();

        var prop = new PropertyDef(
            parent, loader._reader.GetString(info.Name),
            sig.ReturnType, sig.ParameterTypes, sig.Header.IsInstance,
            accs.Getter.IsNil ? null : loader.GetMethod(accs.Getter),
            accs.Setter.IsNil ? null : loader.GetMethod(accs.Setter),
            otherAccessors,
            loader._reader.DecodeConst(info.GetDefaultValue()),
            info.Attributes
        );
        loader.FillCustomAttribs(prop, info.GetCustomAttributes());
        return prop;
    }
}