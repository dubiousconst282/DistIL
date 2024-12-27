namespace DistIL.AsmIO;

using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

public class ModuleResolver : IDisposable
{
    // TODO: Do we need to care about FullName (public keys and versions)?
    internal readonly Dictionary<string, ModuleDef> _cache = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<ResolvingUtils.MethodSelector, MethodDesc> FunctionCache = new();
    internal readonly Dictionary<string, TypeDefOrSpec> TypeCache = new();

    private string[] _searchPaths = [];
    internal readonly ICompilationLogger? _logger;

    /// <summary> A reference to the <c>System.Private.CoreLib</c> assembly. </summary>
    public ModuleDef CoreLib => _coreLib ??= Resolve("System.Private.CoreLib");
    private ModuleDef? _coreLib;

    public SystemTypes SysTypes => _sysTypes ??= new(CoreLib);
    private SystemTypes? _sysTypes;

    public ModuleResolver(ICompilationLogger? logger = null)
    {
        _logger = logger;
    }

    public void AddSearchPaths(IEnumerable<string> paths)
    {
        _searchPaths = _searchPaths
            .Concat(paths)
            .Select(FixRuntimePackRefPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Try to change search path for the `NETCore.App.Ref` pack to the actual implementation path.
        // This is done for a couple reasons:
        // - We make the assumption that "System.Private.CoreLib" always exist, but it doesn't in ref packs.
        //   This would lead to multiple defs for e.g. "System.ValueType", which would cause issues.
        // - We want to depend on _some_ private impl details which are not shipped in ref asms. 
        //   Notably accessing private `List<T>` fields.
        static string FixRuntimePackRefPath(string path)
        {
            // e.g. "C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\7.0.3\ref\net7.0"
            //                              shared                     ****      ***********
            string normPath = Path.GetFullPath(path).Replace('\\', '/');
            string implPath = Regex.Replace(normPath, @"(.+?\/)packs(\/Microsoft\.NETCore\.App)\.Ref(\/.+?)\/.+", "$1shared$2$3");
            return implPath != normPath && Directory.Exists(implPath) ? implPath : path;
        }
    }

    public void AddTrustedSearchPaths()
    {
        // https://docs.microsoft.com/en-us/dotnet/core/dependency-loading/default-probing
        string searchPaths = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        AddSearchPaths(searchPaths.Split(Path.PathSeparator).Select(Path.GetDirectoryName)!);
    }

    public TypeDefOrSpec? Import(Type type, [DoesNotReturnIf(true)] bool throwIfNotFound = true)
    {
        Ensure.That(type.IsTypeDefinition || type.IsConstructedGenericType);

        var mod = Resolve(type.Assembly.GetName());
        TypeDefOrSpec? resolved = null;

        if (mod != null) {
            resolved = type.IsNested || type.IsGenericType
                ? ImportGenericOrNestedType(mod, type, throwIfNotFound)
                : mod.FindType(type.Namespace, type.Name, throwIfNotFound);
        }
        if (resolved == null && throwIfNotFound) {
            throw new InvalidOperationException("Could not import the specified type");
        }
        return resolved;
    }

    private TypeDefOrSpec? ImportGenericOrNestedType(ModuleDef scope, Type type, bool throwIfNotFound)
    {
        var resolved = type.IsNested
            ? (Import(type.DeclaringType!, throwIfNotFound) as TypeDef)?.FindNestedType(type.Name)
            : scope.FindType(type.Namespace, type.Name, throwIfNotFound);

        if (resolved != null && type.IsConstructedGenericType) {
            var genArgs = type.GenericTypeArguments;
            var finalArgs = ImmutableArray.CreateBuilder<TypeDesc>(genArgs.Length);

            foreach (var genArg in genArgs) {
                var resolvedArg = Import(genArg, throwIfNotFound);
                if (resolvedArg == null) {
                    return null;
                }
                finalArgs.Add(resolvedArg);
            }
            return resolved.GetSpec(finalArgs.MoveToImmutable());
        }
        return resolved;
    }

    public ModuleDef? Resolve(AssemblyName name, [DoesNotReturnIf(true)] bool throwIfNotFound = true)
    {
        return Resolve(name.Name ?? throw new InvalidOperationException(), throwIfNotFound);
    }

    public ModuleDef? Resolve(string name, [DoesNotReturnIf(true)] bool throwIfNotFound = true)
    {
        // FIXME: This is not ideal, but it should work fine the module is not saved.
        if (name == "mscorlib") {
            name = "netstandard";
        }

        if (_cache.TryGetValue(name, out var module)) {
            return module;
        }
        module = ResolveImpl(name);

        if (module == null && throwIfNotFound) {
            throw new InvalidOperationException($"Failed to resolve module '{name}'");
        }

        Debug.Assert(module == null || _cache.GetValueOrDefault(name) == module);
        return module!;
    }

    protected virtual ModuleDef? ResolveImpl(string name)
    {
        foreach (string basePath in _searchPaths) {
            string path = Path.Combine(basePath, name + ".dll");
            if (File.Exists(path)) {
                return Load(path);
            }
        }
        return null;
    }

    public ModuleDef Load(string path)
    {
        var module = new ModuleDef(this);
        module._loader = new ModuleLoader(path, module);

        AddToCache(module.AsmName.Name!, module); // AsmName is loaded by ModuleLoader ctor
        _logger?.Debug($"Loading module '{module.AsmName.Name}, v{module.AsmName.Version}'");

        module._loader.Load();

        return module;
    }

    public ModuleDef Create(string asmName, Version? ver = null)
    {
        var module = new ModuleDef(this) {
            AsmName = new AssemblyName() {
                Name = asmName,
                Version = ver ?? new Version(1, 0, 0, 0),
            },
            ModName = asmName + ".dll"
        };
        module.CreateType(null, "<Module>", TypeAttributes.NotPublic); // global type required by the writer
        AddToCache(asmName, module);
        return module;
    }

    private void AddToCache(string name, ModuleDef module)
    {
        _cache.Add(name, module);
    }
    
    public void Dispose()
    {
        foreach (var module in _cache.Values) {
            module._loader!._pe.Dispose();
            module._loader = null;
        }
        _cache.Clear();
        TypeCache.Clear();
        FunctionCache.Clear();
    }
}