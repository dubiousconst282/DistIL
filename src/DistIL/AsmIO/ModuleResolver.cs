namespace DistIL.AsmIO;

using System.IO;
using System.Reflection;
using System.Reflection.PortableExecutable;

public class ModuleResolver
{
    protected readonly Dictionary<string, ModuleDef> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ModuleDef Resolve(AssemblyName name, [MaybeNullWhen(false)] bool throwIfNotFound = true)
    {
        if (_cache.TryGetValue(name.FullName, out var module)) {
            return module;
        }
        module = ResolveImpl(name);

        if (module != null) {
            _cache[name.FullName] = module;
        } else if (throwIfNotFound) {
            throw new InvalidOperationException($"Failed to resolve module '{name}'");
        }
        return module!;
    }

    protected virtual ModuleDef? ResolveImpl(AssemblyName name)
    {
        //https://docs.microsoft.com/en-us/dotnet/core/dependency-loading/default-probing
        string targetName = name.Name!;
        string searchPaths = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;

        foreach (string path in searchPaths.Split(Path.PathSeparator)) {
            var fileName = Path.GetFileNameWithoutExtension(path.AsSpan());
            if (fileName.EqualsIgnoreCase(targetName)) {
                return Load(path);
            }
        }
        return null;
    }

    public ModuleDef Load(string path)
    {
        Console.WriteLine("LoadModule: " + path);
        using var pe = new PEReader(File.OpenRead(path));
        var module = new ModuleDef();
        var loader = new ModuleLoader(pe, this, module);
        _cache.Add(module.AsmName.FullName, module);
        loader.Load();
        return module;
    }
}