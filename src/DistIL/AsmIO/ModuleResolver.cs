namespace DistIL.AsmIO;

using System.IO;
using System.Reflection;
using System.Reflection.PortableExecutable;

public class ModuleResolver
{
    //FIXME: Do we need to care about FullName (public keys and versions)?
    protected readonly Dictionary<string, ModuleDef> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary> A reference to the `System.Private.CoreLib` assembly. </summary>
    public ModuleDef CoreLib => _coreLib ??= Resolve("System.Private.CoreLib", throwIfNotFound: true);
    private ModuleDef? _coreLib;

    public SystemTypes SysTypes => _sysTypes ??= new(CoreLib);
    private SystemTypes? _sysTypes;

    private string[] _searchPaths = { Environment.CurrentDirectory };

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
    public ModuleDef? Resolve(AssemblyName name, [DoesNotReturnIf(true)] bool throwIfNotFound = false)
    {
        return Resolve(name.Name ?? throw new InvalidOperationException(), throwIfNotFound);
    }

    public ModuleDef? Resolve(string name, [DoesNotReturnIf(true)] bool throwIfNotFound = false)
    {
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
        Console.WriteLine("LoadModule: " + path);
        using var pe = new PEReader(File.OpenRead(path), PEStreamOptions.PrefetchEntireImage);
        var module = new ModuleDef() { Resolver = this };
        var loader = new ModuleLoader(pe, this, module);
        _cache.Add(module.AsmName.Name!, module); //AsmName is loaded by ModuleLoader ctor
        loader.Load();
        return module;
    }
}