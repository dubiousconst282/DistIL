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

    internal Dictionary<TypeDef, ModuleDef> _typeRefRoots = new(); //root assemblies for references of forwarded types
    internal Dictionary<CustomAttribLink, CustomAttrib[]> _customAttribs = new();

    public TypeDef? FindType(string? ns, string name, bool includeExports = true, [DoesNotReturnIf(true)] bool throwIfNotFound = false)
    {
        var type = SearchType(_typeDefs, ns, name);

        if (type == null && includeExports) {
            type = SearchType(_exportedTypes, ns, name);
        }
        if (type == null && throwIfNotFound) {
            throw new InvalidOperationException($"Type {ns}.{name} not found");
        }
        return type;
    }

    public TypeDef FindOrCreateType(
        string? ns, string name, 
        TypeAttributes attrs = TypeAttributes.Public,
        TypeDefOrSpec? baseType = null,
        ImmutableArray<GenericParamType> genericParams = default)
    {
        var index = SearchTypeIndex(_typeDefs, ns, name);
        if (index >= 0) {
            return _typeDefs[index];
        }
        var type = new TypeDef(
            this, ns, name, attrs, 
            genericParams.IsDefault ? default : genericParams.CastArray<TypeDesc>(),
            baseType ?? Resolver.SysTypes.Object
        );
        _typeDefs.Insert(~index, type);
        return type;
    }

    public IEnumerable<MethodDef> AllMethods()
        => TypeDefs.SelectMany(t => t.Methods);

    public void Save(Stream stream)
    {
        var builder = new BlobBuilder();
        new ModuleWriter(this).Emit(builder);
        builder.WriteContentTo(stream);
    }

    public override string ToString()
        => AsmName.ToString();

    internal CustomAttrib[] GetLinkedCustomAttribs(in CustomAttribLink link)
        => _customAttribs.GetValueOrDefault(link, Array.Empty<CustomAttrib>());

    internal void SortTypes()
    {
        Comparison<TypeDef> comparer = (a, b) => CompareTypeName(b, a.Namespace, a.Name);
        _typeDefs.Sort(comparer);
        _exportedTypes.Sort(comparer);
    }

    private static TypeDef? SearchType(List<TypeDef> types, string? ns, string name)
    {
        int index = SearchTypeIndex(types, ns, name);
        return index < 0 ? null : types[index];
    }
    private static int SearchTypeIndex(List<TypeDef> types, string? ns, string name)
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
                return mid;
            }
        }
        return ~min;
    }
    private static int CompareTypeName(TypeDef typeA, string? nsB, string nameB)
    {
        int c = string.CompareOrdinal(typeA.Name, nameB);
        return c == 0 ? string.CompareOrdinal(typeA.Namespace, nsB) : c;
    }
}