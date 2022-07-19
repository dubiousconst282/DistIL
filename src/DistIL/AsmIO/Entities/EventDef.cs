namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;

using DistIL.IR;

public class EventDef : MemberDesc
{
    public override TypeDef DeclaringType { get; }
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
}