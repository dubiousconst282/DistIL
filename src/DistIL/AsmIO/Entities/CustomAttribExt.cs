namespace DistIL.AsmIO;

public static class CustomAttribExt
{
    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this ModuleEntity entity)
        => entity.Module.GetLinkedCustomAttribs(new(entity));

    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this TypeDef type, GenericParamType param)
        => type.Module.GetLinkedCustomAttribs(
                new(type, FindIndexOrThrow(type.GenericParams, param), CustomAttribLink.Type.GenericParam));

    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this MethodDef method, GenericParamType param)
        => method.Module.GetLinkedCustomAttribs(
                new(method, FindIndexOrThrow(method.GenericParams, param), CustomAttribLink.Type.GenericParam));

    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this MethodDef method, ParamDef param)
        => method.Module.GetLinkedCustomAttribs(
                new(method, param == method.ReturnParam ? -1 : FindIndexOrThrow(method.Params, param), CustomAttribLink.Type.MethodParam));

    private static int FindIndexOrThrow<T>(ImmutableArray<T> arr, T value)
    {
        int index = arr.IndexOf(value);
        Ensure.That(index >= 0, "Parameter does not exist in the specified method");
        return index;
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