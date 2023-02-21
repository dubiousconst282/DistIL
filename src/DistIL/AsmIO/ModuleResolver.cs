namespace DistIL.AsmIO;

using System.IO;
using System.Reflection;
using System.Reflection.PortableExecutable;

public class ModuleResolver
{
    //FIXME: Do we need to care about FullName (public keys and versions)?
    protected readonly Dictionary<string, ModuleDef> _cache = new(StringComparer.OrdinalIgnoreCase);
    private string[] _searchPaths = Array.Empty<string>();
    private readonly ICompilationLogger? _logger;

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
        _searchPaths = _searchPaths.Concat(paths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void AddTrustedSearchPaths()
    {
        //https://docs.microsoft.com/en-us/dotnet/core/dependency-loading/default-probing
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
            ? (Import(type.DeclaringType!, throwIfNotFound) as TypeDef)?.GetNestedType(type.Name)
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
        //FIXME: This is not ideal, but it should work fine the module is not saved.
        if (name == "mscorlib") {
            name = "netstandard";
        }

        if (_cache.TryGetValue(name, out var module)) {
            return module;
        }
        module = ResolveImpl(name);

        if (module != null) {
            _cache[name] = module;
        } else if (throwIfNotFound) {
            throw new InvalidOperationException($"Failed to resolve module '{name}'");
        }
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
        using var reader = new PEReader(File.OpenRead(path), PEStreamOptions.PrefetchEntireImage);
        return Load(reader);
    }
    public ModuleDef Load(PEReader reader)
    {
        var module = new ModuleDef() { Resolver = this };
        var loader = new ModuleLoader(reader, this, module);

        _cache.Add(module.AsmName.Name!, module); //AsmName is loaded by ModuleLoader ctor
        _logger?.Debug($"Loading module '{module.AsmName.Name}, v{module.AsmName.Version}'");

        loader.Load();

        return module;
    }
}