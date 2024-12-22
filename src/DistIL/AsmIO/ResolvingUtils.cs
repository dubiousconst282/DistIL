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
            if (method.ParamSig.Select((type, index) => (i: index, e: type)).Any(_ => _.e.Type != convertedSelector.ParameterTypes[_.i])) {
                continue;
            }

            if (selector.ReturnType is not null) {
                if (method.ReturnType != selector.ReturnType) {
                    continue;
                }
            }

            resolver.FunctionCache.Add(selector, method);
            return method;
        }

        return methods.FirstOrDefault();
    }

    private static MethodSelector GetSelector(this ModuleResolver resolver, string selector)
    {
        var spl = selector.Split("::");
        var type = FindType(resolver, spl[0].Trim());

        var methodPart = spl[1].Trim();
        var methodName = methodPart.Contains('(') ? methodPart[..methodPart.IndexOf('(')] : methodPart;

        var parameterPart = methodPart[methodName.Length..].Trim('(', ')');

        string? returnTypeString = null;
        if (parameterPart.Contains("->"))
        {
            var parts = parameterPart.Split("->", StringSplitOptions.RemoveEmptyEntries);
            parameterPart = parts[0].Trim();
            returnTypeString = parts.Length > 1 ? parts[1].Trim() : null;
        }

        TypeDesc? GetParameterType(string? fullname)
        {
            if (fullname is null) {
                return null;
            }

            return fullname == "this" ? type : FindType(resolver, fullname.Trim());
        }

        var parameterTypes = parameterPart
            .Split(",", StringSplitOptions.RemoveEmptyEntries)
            .Select(GetParameterType)
            .ToList();

        return new MethodSelector(type, methodName, parameterTypes, GetParameterType(returnTypeString));
    }

   /// <summary>
    /// Finds a type based on its full name.
    /// </summary>
    /// <param name="resolver">The module resolver.</param>
    /// <param name="fullname">The full name of the type to find.</param>
    /// <example>
    /// <code>
    /// var type = resolver.FindType("System.Text.StringBuilder");
    /// </code>
    /// </example>
    /// <returns>The type descriptor if found; otherwise, null.</returns>
    public static TypeDesc? FindType(this ModuleResolver resolver, string fullname)
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

        foreach (var type in resolver._cache.Values
                     .Select(module => module.FindType(ns, typeName))
                     .OfType<TypeDef>()) {
            resolver.TypeCache.Add(fullname, type);
            return type;
        }

        return null;
    }

    internal record MethodSelector(TypeDesc? Type, string MethodName, List<TypeDesc?> ParameterTypes, TypeDesc? ReturnType)
    {
        public override int GetHashCode()
        {
            int hash = 5;

            foreach (var parameterType in ParameterTypes) {
                hash = HashCode.Combine(hash, parameterType);
            }

            hash = HashCode.Combine(hash, Type, MethodName, ReturnType);

            return hash;
        }

        public virtual bool Equals(MethodSelector? other)
        {
            return other != null 
                   && MethodName.Equals(other.MethodName)
                   && Type == other.Type
                   && ParameterTypes.SequenceEqual(other.ParameterTypes);
        }
    }
}