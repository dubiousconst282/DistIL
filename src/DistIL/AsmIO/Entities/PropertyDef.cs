namespace DistIL.AsmIO;

using System.Reflection;

public class PropertyDef : MemberDesc
{
    public override TypeDef DeclaringType { get; }
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
}