namespace DistIL.AsmIO;

public interface Entity
{
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

    [Obsolete("This property always returns `PrimType.Void` for `EntityDesc` objects, and can be confused with `MethodDesc.ReturnType`.")]
    public new TypeDesc ResultType => base.ResultType;

    [Obsolete("This property always returns `false` for `EntityDesc` objects.")]
    public new bool HasResult => base.HasResult;
}

public abstract class MemberDesc : EntityDesc
{
    public abstract TypeDesc DeclaringType { get; }
}