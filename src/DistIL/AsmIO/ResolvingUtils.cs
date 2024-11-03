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
        if (resolver.FunctionCache.TryGetValue(selector, out MethodDesc? cachedMethod)) {
            return cachedMethod;
        }

        var convertedSelector = GetSelector(resolver, selector);

        if (convertedSelector.Type == null) {
            return null;
        }

        var methods = convertedSelector.Type.Methods
            .Where(_ => _.Name.ToString() == convertedSelector.FunctionName)
            .Where(_ => _.ParamSig.Count == convertedSelector.ParameterTypes.Length).ToArray();

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
        var ms = new MethodSelector();

        var spl = selector.Split("::");

        ms.Type = GetTypeSpec(resolver, spl[0].Trim());

        var methodPart = spl[1].Trim();
        ms.FunctionName = methodPart[..methodPart.IndexOf('(')];

        var parameterPart = methodPart[ms.FunctionName.Length..].Trim('(', ')');

        TypeDesc? GetParameterType(string fullname)
        {
            return fullname == "this" ? ms.Type : GetTypeSpec(resolver, fullname.Trim());
        }

        ms.ParameterTypes = parameterPart
            .Split(",", StringSplitOptions.RemoveEmptyEntries)
            .Select(GetParameterType)
            .ToArray();

        return ms;
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

    private class MethodSelector
    {
        public TypeDesc? Type { get; set; }
        public string FunctionName { get; set; }
        public TypeDesc?[] ParameterTypes { get; set; }
    }
}