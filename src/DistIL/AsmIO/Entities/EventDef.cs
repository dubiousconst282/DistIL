namespace DistIL.AsmIO;

using System.Reflection;

public class EventDef : MemberDesc, ModuleEntity
{
    public override TypeDef DeclaringType { get; }
    public ModuleDef Module => DeclaringType.Module;
    public override string Name { get; set; }

    public EventAttributes Attribs { get; set; }

    public TypeDesc Type { get; set; }

    public MethodDef? Adder { get; set; }
    public MethodDef? Remover { get; set; }
    public MethodDef? Raiser { get; set; }
    public MethodDef[] OtherAccessors { get; set; }

    internal IList<CustomAttrib>? _customAttribs;

    public EventDef(
        TypeDef declaryingType, string name, TypeDesc type,
        MethodDef? adder = null, MethodDef? remover = null, MethodDef? raiser = null,
        MethodDef[]? otherAccessors = null,
        EventAttributes attribs = default)
    {
        DeclaringType = declaryingType;
        Name = name;
        Type = type;
        Adder = adder;
        Remover = remover;
        Raiser = raiser;
        OtherAccessors = otherAccessors ?? [];
        Attribs = attribs;
    }

    public IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => CustomAttribUtils.GetOrInitList(ref _customAttribs, readOnly);
    
    public override void Print(PrintContext ctx)
    {
        ctx.Print($"{DeclaringType.Name}::{Name} {{");
        if (Adder != null) ctx.Print($" add => {Adder};");
        if (Remover != null) ctx.Print($" remove => {Remover};");
        if (Raiser != null) ctx.Print($" raise => {Raiser};");
        ctx.Print(" }");
    }
}