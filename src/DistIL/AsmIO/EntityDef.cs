namespace DistIL.AsmIO;

using DistIL.IR;

public interface Entity
{
    string Name { get; }
    ImmutableArray<CustomAttrib> CustomAttribs { get; set; }
}
/// <summary> Represents an entity defined in a module. </summary>
public interface ModuleEntity : Entity
{
    ModuleDef Module { get; }
}
/// <summary> Represents an entity referenced or defined in a module. </summary>
public abstract class EntityDesc : Value, Entity
{
    public abstract string Name { get; }
    public ImmutableArray<CustomAttrib> CustomAttribs { get; set; } = ImmutableArray<CustomAttrib>.Empty;
}

public abstract class MemberDesc : EntityDesc
{
    public abstract TypeDesc DeclaringType { get; }
}