namespace DistIL.AsmIO;

public static class ResolvingUtils
{
    /// <summary>
    /// Finds a method based on a selector
    /// </summary>
    /// <param name="resolver"></param>
    /// <param name="selector">Specifies which method to select</param>
    /// <example>FindMethod("System.Text.StringBuilder::AppendLine(this, System.String)")</example>
    /// <returns></returns>
    public static MethodDesc? FindMethod(this ModuleResolver resolver, string selector)
    {
        var convertedSelector = GetSelector(resolver, selector);
        if (resolver.FunctionCache.TryGetValue(convertedSelector, out MethodDesc? cachedMethod)) {
            return cachedMethod;
        }

        if (convertedSelector.Type == null) {
            return null;
        }

        return ResolveMethod(resolver, convertedSelector, convertedSelector);
    }

    /// <summary>
    /// Finds a method based on a selector and arguments
    /// </summary>
    /// <param name="resolver"></param>
    /// <param name="selector">Specifies which method to select</param>
    /// <example>FindMethod("System.Text.StringBuilder::AppendLine", [ConstString.Create("Hello World")])</example>
    /// <returns></returns>
    public static MethodDesc? FindMethod(this ModuleResolver resolver, string selector, IEnumerable<Value> values)
    {
        var convertedSelector = GetSelector(resolver, selector);
        convertedSelector.ParameterTypes.AddRange(values.Select(value => value.ResultType));

        if (resolver.FunctionCache.TryGetValue(convertedSelector, out MethodDesc? cachedMethod)) {
            return cachedMethod;
        }

        if (convertedSelector.Type == null) {
            return null;
        }

        return ResolveMethod(resolver, convertedSelector, convertedSelector);
    }

    private static MethodDesc? ResolveMethod(ModuleResolver resolver, MethodSelector selector, MethodSelector convertedSelector)
    {
        var methods = convertedSelector.Type!.Methods
            .Where(method => method.Name.ToString() == convertedSelector.MethodName)
            .Where(method => method.ParamSig.Count == convertedSelector.ParameterTypes.Count).ToArray();

        foreach (var method in methods) {
            for (int i = 0; i < method.ParamSig.Count; i++) {
                if (method.ParamSig[i].Type == convertedSelector.ParameterTypes[i]) {
                    resolver.FunctionCache.AddOrUpdate(selector, _ => method, (_, oldMethod) => oldMethod);

                    return method;
                }
            }
        }

        return methods.FirstOrDefault();
    }

    private static MethodSelector GetSelector(this ModuleResolver resolver, string selector)
    {
        var spl = selector.Split("::");

        var type = GetTypeSpec(resolver, spl[0].Trim());

        var methodPart = spl[1].Trim();
        var methodName = methodPart.Contains('(') ? methodPart[..methodPart.IndexOf('(')] : methodPart;

        var parameterPart = methodPart[methodName.Length..].Trim('(', ')');

        TypeDesc? GetParameterType(string fullname)
        {
            return fullname == "this" ? type : GetTypeSpec(resolver, fullname.Trim());
        }

        var parameterTypes = parameterPart
            .Split(",", StringSplitOptions.RemoveEmptyEntries)
            .Select(GetParameterType)
            .ToList();

        return new MethodSelector(type, methodName, parameterTypes);
    }

    private static TypeDesc? GetTypeSpec(this ModuleResolver resolver, string fullname)
    {
        var primType = PrimType.GetFromAlias(fullname);

        if (primType != null) {
            return primType;
        }

        if (resolver.TypeCache.TryGetValue(fullname, out var cachedType)) {
            return cachedType;
        }

        var spl = fullname.Split(".");
        var typeName = spl.Last();
        var ns = string.Join('.', spl[..^1]);

        foreach (var type in resolver._loadedModules
                     .Select(module => module.FindType(ns, typeName))
                     .OfType<TypeDef>()) {
            resolver.TypeCache.AddOrUpdate(fullname, _ => type, (_, oldType) => oldType);
            return type;
        }

        return null;
    }

    internal record MethodSelector(TypeDesc? Type, string MethodName, List<TypeDesc?> ParameterTypes)
    {
    }
}