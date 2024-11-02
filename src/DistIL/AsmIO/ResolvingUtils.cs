namespace DistIL.AsmIO;

using System.Collections.Concurrent;

public static class ResolvingUtils
{
    private static readonly ConcurrentDictionary<string, MethodDesc> FunctionCache = new();
    private static readonly ConcurrentDictionary<string, TypeDefOrSpec> TypeCache = new();

    /// <summary>
    /// Finds a method based on a selector
    /// </summary>
    /// <param name="binder"></param>
    /// <param name="selector">Specifies which method to select</param>
    /// <example>FindMethod("System.Text.StringBuilder::AppendLine(System.String)")</example>
    /// <returns></returns>
    public static MethodDesc? FindMethod(this ModuleResolver resolver, string selector)
    {
        if (FunctionCache.TryGetValue(selector, out MethodDesc? cachedMethod))
        {
            return cachedMethod;
        }

        var convertedSelector = GetSelector(resolver, selector);

        if(convertedSelector.Type == null) {
            return null;
        }

        var methods = convertedSelector.Type.Methods
            .Where(_ => _.Name.ToString() == convertedSelector.FunctionName)
            .Where(_ => _.ParamSig.Count == convertedSelector.ParameterTypes.Length);

        foreach (var method in methods)
        {
            for (int i = 0; i < method.ParamSig.Count; i++)
            {
                if (method.ParamSig[i].Type == convertedSelector.ParameterTypes[i])
                {
                    FunctionCache.AddOrUpdate(selector, _ => method, (_, oldMethod) => oldMethod);

                    return method;
                }
            }
        }

        return methods.FirstOrDefault();
    }

    private static FunctionSelector GetSelector(this ModuleResolver resolver, string selector)
    {
        var ms = new FunctionSelector();

        var spl = selector.Split("::");

        ms.Type = GetTypeSpec(resolver, spl[0]);

        var methodPart = spl[1];
        ms.FunctionName = methodPart[..methodPart.IndexOf('(')];

        var parameterPart = methodPart[ms.FunctionName.Length..].Trim('(', ')');

        TypeDefOrSpec? GetParameterType(string fullname)
        {
            if (fullname == "this") {
                return ms.Type;
            }

            return GetTypeSpec(resolver, fullname);
        }

        ms.ParameterTypes = parameterPart
            .Split(",", StringSplitOptions.RemoveEmptyEntries)
            .Select(GetParameterType)
            .ToArray();

        return ms;
    }

    private static TypeDefOrSpec? GetTypeSpec(this ModuleResolver resolver, string fullname)
    {
        if (TypeCache.TryGetValue(fullname, out var cachedType))
        {
            return cachedType;
        }

        var spl = fullname.Split(".");
        var typeName = spl.Last();
        var ns = string.Join('.', spl[..^1]);

        foreach (var type in resolver._loadedModules
                     .Select(module => module.FindType(ns, typeName))
                     .OfType<TypeDef>())
        {
            TypeCache.AddOrUpdate(fullname, _ => type, (_, oldType) => oldType);
            return type;
        }

        return null;
    }

    private class FunctionSelector
    {
        public TypeDefOrSpec? Type { get; set; }
        public string FunctionName { get; set; }
        public TypeDefOrSpec?[] ParameterTypes { get; set; }
    }
}