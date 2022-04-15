namespace DistIL.AsmIO;

using System.IO;
using System.Reflection;
using System.Reflection.PortableExecutable;

public class ModuleResolver : IDisposable
{
    protected readonly Dictionary<string, ModuleDef> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PEReader> _openPEs = new();

    public void Register(AssemblyName name, string path)
    {
        _cache[name.FullName] = LoadModule(path);
    }

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
                return LoadModule(path);
            }
        }
        return null;
    }

    private ModuleDef LoadModule(string path)
    {
        var pe = new PEReader(File.OpenRead(path), PEStreamOptions.PrefetchEntireImage);
        _openPEs.Add(pe);
        return new ModuleDef(pe, this);
    }

    public void Dispose()
    {
        foreach (var pe in _openPEs) {
            pe.Dispose();
        }
        _openPEs.Clear();
        _cache.Clear();
    }
}