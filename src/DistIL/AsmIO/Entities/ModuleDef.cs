namespace DistIL.AsmIO;

using System.IO;
using System.Reflection;
using System.Reflection.Metadata;

public class ModuleDef : ModuleEntity
{
    public string ModName { get; set; } = null!;
    public AssemblyName AsmName { get; set; } = null!;
    public AssemblyFlags AsmFlags { get; set; }

    public MethodDef? EntryPoint { get; set; }
    public IReadOnlyList<TypeDef> TypeDefs => _typeDefs;
    public IReadOnlyList<TypeDef> ExportedTypes => _exportedTypes;

    public ModuleResolver Resolver { get; init; } = null!;

    ModuleDef ModuleEntity.Module => this;

    internal List<TypeDef> _typeDefs = new(), _exportedTypes = new();
    internal bool _typesSorted = false;

    internal Dictionary<TypeDef, ModuleDef> _typeRefRoots = new(); //root assemblies for references of forwarded types
    internal Dictionary<CustomAttribLink, CustomAttrib[]> _customAttribs = new();

    public TypeDef? FindType(string? ns, string name, bool includeExports = true, [DoesNotReturnIf(true)] bool throwIfNotFound = false)
    {
        if (!_typesSorted) {
            _typesSorted = true;

            Comparison<TypeDef> comparer = (a, b) => CompareTypeName(b, a.Namespace, a.Name);
            _typeDefs.Sort(comparer);
            _exportedTypes.Sort(comparer);
        }
        var type = SearchType(_typeDefs, ns, name);

        if (type == null && includeExports) {
            type = SearchType(_exportedTypes, ns, name);
        }
        if (type == null && throwIfNotFound) {
            throw new InvalidOperationException($"Type {ns}.{name} not found");
        }
        return type;
    }

    private static TypeDef? SearchType(List<TypeDef> types, string? ns, string name)
    {
        int min = 0, max = types.Count - 1;
        while (min <= max) {
            int mid = (min + max) >>> 1;
            int c = CompareTypeName(types[mid], ns, name);

            if (c < 0) {
                max = mid - 1;
            } else if (c > 0) {
                min = mid + 1;
            } else {
                return types[mid];
            }
        }
        return null;
    }
    private static int CompareTypeName(TypeDef typeA, string? nsB, string nameB)
    {
        int c = string.CompareOrdinal(typeA.Name, nameB);
        return c == 0 ? string.CompareOrdinal(typeA.Namespace, nsB) : c;
    }

    public IEnumerable<MethodDef> AllMethods()
        => TypeDefs.SelectMany(t => t.Methods);

    internal CustomAttrib[] GetLinkedCustomAttribs(in CustomAttribLink link)
        => _customAttribs.GetValueOrDefault(link, Array.Empty<CustomAttrib>());

    public void Save(Stream stream)
    {
        var builder = new BlobBuilder();
        new ModuleWriter(this).Emit(builder);
        builder.WriteContentTo(stream);
    }

    public override string ToString() => AsmName.ToString();
}