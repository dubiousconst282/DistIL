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

public static class EntityExt
{
    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this ModuleEntity entity)
        => entity.Module.GetCustomAttribs(new() { Entity = entity });
}