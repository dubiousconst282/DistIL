namespace DistIL.AsmIO;

using System.IO;
using System.Reflection;
using System.Reflection.PortableExecutable;

public class ModuleResolver
{
    //FIXME: Do we need to care about FullName (public keys and versions?)
    protected readonly Dictionary<string, ModuleDef> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ModuleDef Resolve(AssemblyName name, [MaybeNullWhen(false)] bool throwIfNotFound = true)
    {
        return Resolve(name.Name ?? throw new ArgumentException(), throwIfNotFound);
    }

    public ModuleDef Resolve(string name, [MaybeNullWhen(false)] bool throwIfNotFound = true)
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
        //https://docs.microsoft.com/en-us/dotnet/core/dependency-loading/default-probing
        string searchPaths = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;

        foreach (string path in searchPaths.Split(Path.PathSeparator)) {
            var fileName = Path.GetFileNameWithoutExtension(path.AsSpan());
            if (fileName.EqualsIgnoreCase(name)) {
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