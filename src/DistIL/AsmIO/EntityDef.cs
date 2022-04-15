namespace DistIL.AsmIO;

public interface EntityDef
{
    public ModuleDef Module { get; }
}

public interface MemberDef : EntityDef
{
    TypeDef DeclaringType { get; }
    string Name { get; }
}