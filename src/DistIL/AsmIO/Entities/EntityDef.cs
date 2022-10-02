namespace DistIL.AsmIO;

using DistIL.IR;

public interface Entity
{
    string Name { get; }
}
/// <summary> Represents an entity defined in a module. </summary>
public interface ModuleEntity : Entity
{
    ModuleDef Module { get; }

    IReadOnlyCollection<CustomAttrib> GetCustomAttribs()
        => Module.GetCustomAttribs(new() { LinkType = CustomAttribLink.Type.Entity, Entity = this });
}
/// <summary> Represents an entity referenced or defined in a module. </summary>
public abstract class EntityDesc : Value, Entity
{
    public abstract string Name { get; }
}

public abstract class MemberDesc : EntityDesc
{
    public abstract TypeDesc DeclaringType { get; }
}