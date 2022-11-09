namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;

public class EventDef : MemberDesc, ModuleEntity
{
    public override TypeDef DeclaringType { get; }
    public ModuleDef Module => DeclaringType.Module;

    public EventAttributes Attribs { get; init; }
    public override string Name { get; }

    public TypeDesc Type { get; }

    public MethodDef? Adder { get; }
    public MethodDef? Remover { get; }
    public MethodDef? Raiser { get; }
    public ImmutableArray<MethodDef> OtherAccessors { get; }

    public EventDef(
        TypeDef declaryingType, string name, TypeDesc type,
        MethodDef? adder = null, MethodDef? remover = null, MethodDef? raiser = null,
        ImmutableArray<MethodDef> otherAccessors = default,
        EventAttributes attribs = default)
    {
        DeclaringType = declaryingType;
        Name = name;
        Type = type;
        Adder = adder;
        Remover = remover;
        Raiser = raiser;
        OtherAccessors = otherAccessors.EmptyIfDefault();
        Attribs = attribs;
    }

    public override void Print(PrintContext ctx)
    {
        ctx.Print($"{DeclaringType.Name}::{Name} {{");
        if (Adder != null) ctx.Print($" add => {Adder};");
        if (Remover != null) ctx.Print($" remove => {Remover};");
        if (Raiser != null) ctx.Print($" raise => {Raiser};");
        ctx.Print(" }");
    }

    internal static EventDef Decode3(ModuleLoader loader, EventDefinitionHandle handle, TypeDef parent)
    {
        var info = loader._reader.GetEventDefinition(handle);
        var type = (TypeDesc)loader.GetEntity(info.Type);
        var accs = info.GetAccessors();
        var otherAccessors = accs.Others.IsEmpty
            ? default(ImmutableArray<MethodDef>)
            : accs.Others.Select(loader.GetMethod).ToImmutableArray();

        var evt = new EventDef(
            parent, loader._reader.GetString(info.Name), type,
            accs.Adder.IsNil ? null : loader.GetMethod(accs.Adder),
            accs.Remover.IsNil ? null : loader.GetMethod(accs.Remover),
            accs.Raiser.IsNil ? null : loader.GetMethod(accs.Raiser),
            otherAccessors,
            info.Attributes
        );
        loader.FillCustomAttribs(evt, info.GetCustomAttributes());
        return evt;
    }
}