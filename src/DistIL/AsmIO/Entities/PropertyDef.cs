namespace DistIL.AsmIO;

using System.Reflection;

public class PropertyDef : MemberDesc, ModuleEntity
{
    public override TypeDef DeclaringType { get; }
    public ModuleDef Module => DeclaringType.Module;
    public override string Name { get; set; }

    public PropertyAttributes Attribs { get; set; }
    
    public MethodSig Sig { get; set; }

    public object? DefaultValue { get; set; }

    public MethodDef? Getter { get; set; }
    public MethodDef? Setter { get; set; }
    public MethodDef[] OtherAccessors { get; set; }

    internal IList<CustomAttrib>? _customAttribs;

    internal PropertyDef(
        TypeDef declaringType, string name, MethodSig sig,
        MethodDef? getter = null, MethodDef? setter = null,
        MethodDef[]? otherAccessors = null,
        object? defaultValue = null,
        PropertyAttributes attribs = default)
    {
        DeclaringType = declaringType;
        Name = name;
        Sig = sig;
        Getter = getter;
        Setter = setter;
        OtherAccessors = otherAccessors ?? [];
        DefaultValue = defaultValue;
        Attribs = attribs;
    }

    public IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => CustomAttribUtils.GetOrInitList(ref _customAttribs, readOnly);

    public override void Print(PrintContext ctx)
    {
        ctx.Print($"{DeclaringType.Name}::{Name} {{");
        if (Getter != null) ctx.Print($" get => {Getter};");
        if (Setter != null) ctx.Print($" set => {Setter};");
        ctx.Print(" }");
    }
}