namespace DistIL.AsmIO;

using System.IO;
using System.Reflection;
using System.Reflection.Metadata;

public class ModuleDef : ModuleEntity
{
    public string Name { get; set; } = null!;
    public AssemblyName AsmName { get; set; } = null!;
    public AssemblyFlags AsmFlags { get; set; }

    public MethodDef? EntryPoint { get; set; }
    public List<TypeDef> TypeDefs { get; } = new();
    public List<TypeDef> ExportedTypes { get; } = new();

    public ModuleResolver Resolver { get; init; } = null!;

    public SystemTypes SysTypes { get; internal set; } = null!;

    ModuleDef ModuleEntity.Module => this;

    internal Dictionary<TypeDef, ModuleDef> _typeRefRoots = new(); //root assemblies for references of forwarded types
    internal Dictionary<CustomAttribLink, CustomAttrib[]> _customAttribs = new();

    internal TypeDef? FindType(string? ns, string name, bool includeExports = true, [DoesNotReturnIf(true)] bool throwIfNotFound = false)
    {
        var availableTypes = includeExports ? TypeDefs.Concat(ExportedTypes) : TypeDefs;
        foreach (var type in availableTypes) {
            if (type.Name == name && type.Namespace == ns) {
                return type;
            }
        }
        if (throwIfNotFound) {
            throw new InvalidOperationException($"Type {ns}.{name} not found");
        }
        return null;
    }

    public TypeDesc? Import(Type type, [DoesNotReturnIf(true)] bool throwIfNotFound = false)
    {
        var mod = Resolver.Resolve(type.Assembly.GetName());
        return mod?.FindType(type.Namespace, type.Name, throwIfNotFound);
    }

    /// <summary> Imports the module/assembly with the given name. </summary>
    public ModuleDef? ImportModule(string name, [DoesNotReturnIf(true)] bool throwIfNotFound = false)
    {
        return Resolver.Resolve(name, throwIfNotFound);
    }

    public IEnumerable<MethodDef> AllMethods()
    {
        foreach (var type in AllTypes()) {
            foreach (var method in type.Methods) {
                yield return method;
            }
        }
    }
    public IEnumerable<TypeDef> AllTypes()
    {
        return TypeDefs;
    }

    internal CustomAttrib[] GetCustomAttribs(in CustomAttribLink link)
        => _customAttribs.GetValueOrDefault(link, Array.Empty<CustomAttrib>());

    public void Save(Stream stream)
    {
        var builder = new BlobBuilder();
        new ModuleWriter(this).Emit(builder);
        builder.WriteContentTo(stream);
    }

    public override string ToString() => AsmName.ToString();
}