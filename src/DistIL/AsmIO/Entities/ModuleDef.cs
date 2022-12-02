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
    internal List<CustomAttrib> _asmCustomAttribs = new(), _modCustomAttribs = new();

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

    public TypeDef CreateType(
        string? ns, string name, 
        TypeAttributes attrs = TypeAttributes.Public,
        TypeDefOrSpec? baseType = null,
        ImmutableArray<GenericParamType> genericParams = default)
    {
        var index = SearchTypeIndex(_typeDefs, ns, name);
        if (index >= 0) {
            throw new InvalidOperationException("A type with the same name already exists");
        }
        var type = new TypeDef(
            this, ns, name, attrs, genericParams,
            baseType ?? Resolver.SysTypes.Object
        );
        _typeDefs.Insert(~index, type);
        return type;
    }

    public IEnumerable<MethodDef> AllMethods()
        => TypeDefs.SelectMany(t => t.Methods);

    public List<CustomAttrib> GetCustomAttribs(bool forAssembly)
        => forAssembly ? _asmCustomAttribs : _modCustomAttribs;

    public void Save(Stream stream)
    {
        var builder = new BlobBuilder();
        new ModuleWriter(this).Emit(builder);
        builder.WriteContentTo(stream);
    }
    public void Save(string filename)
    {
        using var stream = File.Create(filename);
        Save(stream);
    }

    public override string ToString()
        => AsmName.ToString();

    internal void SortTypes()
    {
        _typeDefs.Sort(CompareTypeName);
        _exportedTypes.Sort(CompareTypeName);
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
            int c = -CompareTypeName(types[mid], ns, name);

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
    private static int CompareTypeName(TypeDef typeA, string? nsB, string nameB, bool weightUpNested = true)
    {
        //Nested types are all at the end, and can never match a name
        if (typeA.IsNested && weightUpNested) {
            return +1;
        }
        int c = string.CompareOrdinal(typeA.Name, nameB);

        if (c == 0) {
            c = string.CompareOrdinal(typeA.Namespace, nsB);
        }
        //Force global type to always be the first item on the table
        if (c != 0 && (typeA.Name == "<Module>" || nameB == "<Module>")) {
            return nameB == "<Module>" ? +1 : -1;
        }
        return c;
    }
    internal static int CompareTypeName(TypeDef typeA, TypeDef typeB)
    {
        int c = typeA.IsNested.CompareTo(typeB.IsNested);
        return c == 0 ? CompareTypeName(typeA, typeB.Namespace, typeB.Name, false) : c;
    }
}