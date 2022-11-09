namespace DistIL.AsmIO;

public static class CustomAttribExt
{
    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this ModuleEntity entity)
        => entity.Module.GetLinkedCustomAttribs(new(entity));

    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this TypeDef type, GenericParamType param)
        => GetLinkedAttribs(type, type.GenericParams.IndexOf(param), CustomAttribLink.Type.GenericParam);

    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this MethodDef method, GenericParamType param)
        => GetLinkedAttribs(method, method.GenericParams.IndexOf(param), CustomAttribLink.Type.GenericParam);

    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this MethodDef method, ParamDef param)
        => GetLinkedAttribs(method, method.Params.IndexOf(param), CustomAttribLink.Type.MethodParam);

    private static IReadOnlyCollection<CustomAttrib> GetLinkedAttribs(ModuleEntity entity, int index, CustomAttribLink.Type type)
    {
        Ensure.That(index >= 0, "Parameter does not exist in the specified method");
        return entity.Module.GetLinkedCustomAttribs(new(entity, index, type));
    }

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