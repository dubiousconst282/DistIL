namespace DistIL.AsmIO;

public static class CustomAttribExt
{
    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this Entity entity)
    {
        return entity switch {
            ModuleEntity c
                => c.Module.GetLinkedCustomAttribs(new(c)),

            GenericParamType { _customAttribs: not null } c
                => c._customAttribs,

            _ => Array.Empty<CustomAttrib>()
        };
    }

    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this TypeDef type, GenericParamType param)
    {
        int index = FindIndexOrThrow(type.GenericParams, param);
        return type.Module.GetLinkedCustomAttribs(new(type, index, CustomAttribLink.Type.GenericParam));
    }

    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this MethodDef method, GenericParamType param)
    {
        int index = FindIndexOrThrow(method.GenericParams, param);
        return method.Module.GetLinkedCustomAttribs(new(method, index, CustomAttribLink.Type.GenericParam));
    }

    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this MethodDef method, ParamDef param)
    {
        int index = param == method.ReturnParam ? -1 : FindIndexOrThrow(method.Params, param);
        return method.Module.GetLinkedCustomAttribs(new(method, index, CustomAttribLink.Type.MethodParam));
    }

    public static IReadOnlyCollection<CustomAttrib> GetCustomAttribs(this GenericParamType genPar, TypeSig constraint)
    {
        int index = FindIndexOrThrow(genPar.Constraints, constraint);
        return genPar._constraintCustomAttribs?.ElementAtOrDefault(index) ?? Array.Empty<CustomAttrib>();
    }

    private static int FindIndexOrThrow<T>(ImmutableArray<T> arr, T value)
    {
        int index = arr.IndexOf(value);
        Ensure.That(index >= 0, "Parameter does not exist in the specified method");
        return index;
    }

    public static CustomAttrib? GetCustomAttrib(this Entity entity, string className)
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