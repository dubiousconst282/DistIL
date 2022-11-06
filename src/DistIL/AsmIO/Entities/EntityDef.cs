namespace DistIL.AsmIO;

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

    [Obsolete("This property always returns `PrimType.Void` for `EntityDesc` objects, and can be confused with `MethodDesc.ReturnType`.")]
    public new TypeDesc ResultType => base.ResultType;

    [Obsolete("This property always returns `false` for `EntityDesc` objects.")]
    public new bool HasResult => base.HasResult;
}

public abstract class MemberDesc : EntityDesc
{
    public abstract TypeDesc DeclaringType { get; }
}

public static class EntityExt
{
    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this ModuleEntity entity)
        => entity.Module.GetCustomAttribs(new() { Entity = entity });

    public static CustomAttrib? GetCustomAttrib(this ModuleEntity entity, string className)
    {
        int nsEnd = className.LastIndexOf('.');

        return entity.GetCustomAttribs().FirstOrDefault(ca => {
            var declType = ca.Constructor.DeclaringType;
            if (nsEnd > 0) {
                return className.AsSpan(0, nsEnd).Equals(declType.Namespace, StringComparison.Ordinal) &&
                       className.AsSpan(nsEnd + 1).Equals(declType.Name, StringComparison.Ordinal);
            }
            return className.Equals(declType.Name);
        });
    }
}