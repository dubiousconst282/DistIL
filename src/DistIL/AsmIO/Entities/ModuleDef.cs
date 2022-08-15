namespace DistIL.AsmIO;

using System.IO;
using System.Reflection;
using System.Reflection.Metadata;

public class ModuleDef : ModuleEntity
{
    public string Name { get; set; } = null!;
    public AssemblyName AsmName { get; set; } = null!;
    public AssemblyFlags AsmFlags { get; set; }
    public ImmutableArray<CustomAttrib> CustomAttribs { get; set; } = ImmutableArray<CustomAttrib>.Empty;

    public MethodDef? EntryPoint { get; set; }
    public List<ModuleDef> AssemblyRefs { get; } = new();
    public List<TypeDef> TypeDefs { get; } = new();
    public List<TypeDef> ExportedTypes { get; } = new();

    internal Dictionary<TypeDef, ModuleDef> _typeRefRoots = new(); //root assemblies for references of forwarded types

    public ModuleResolver Resolver { get; init; } = null!;

    /// <summary> The resolved `System.Runtime` or `System.Private.CoreLib` module reference. </summary>
    public ModuleDef CoreLib { get; internal set; } = null!;
    public SystemTypes SysTypes { get; internal set; } = null!;

    ModuleDef ModuleEntity.Module => this;

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

    public TypeDesc Import(Type type)
    {
        //TODO: add new references
        return FindReferencedType(type) ?? throw new NotImplementedException();
    }

    /// <summary> Imports the module/assembly with the given name. </summary>
    public ModuleDef ImportModule(string name)
    {
        //TODO: add new references
        return AssemblyRefs.First(m => m.AsmName.Name == name);
    }

    public TypeDesc? GetImport(Type type)
    {
        return FindReferencedType(type);
    }

    private TypeDef? FindReferencedType(Type type)
    {
        var asmName = type.Assembly.GetName().Name;
        bool isCoreLib = type.Assembly == typeof(int).Assembly;
        
        foreach (var mod in AssemblyRefs) {
            if (mod.AsmName.Name == asmName) {
                return mod.FindType(type.Namespace, type.Name);
            }
            //TODO: find a prettier way to handle aliasing between System.Runtime and System.Private.CoreLib
            if (mod.ExportedTypes.Count > 0) {
                foreach (var expMod in mod.AssemblyRefs) {
                    if (expMod.AsmName.Name == asmName) {
                        return expMod.FindType(type.Namespace, type.Name);
                    }
                }
            }
        }
        return null;
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

    public void Save(Stream stream)
    {
        var builder = new BlobBuilder();
        new ModuleWriter(this).Emit(builder);
        builder.WriteContentTo(stream);
    }

    public override string ToString() => AsmName.ToString();
}