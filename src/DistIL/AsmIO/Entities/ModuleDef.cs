namespace DistIL.AsmIO;

using System.Collections;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;

public class ModuleDef : EntityDesc
{
    public string ModName { get; set; } = null!;
    public AssemblyName AsmName { get; set; } = null!;
    public AssemblyFlags AsmFlags { get; set; }

    public MethodDef? EntryPoint { get; set; }

    /// <summary> All types defined in this module (incl. nested). </summary>
    public IReadOnlyCollection<TypeDef> TypeDefs => _typeDefs;

    /// <summary> All types exported by this module (incl. nested). </summary>
    public IReadOnlyCollection<TypeDef> ExportedTypes => _exportedTypes;

    public ModuleResolver Resolver { get; }

    internal TypeList _typeDefs = new(), _exportedTypes = new();

    internal Dictionary<TypeDef, ModuleDef> _typeRefRoots = new(); //root assemblies for references of forwarded types
    internal List<CustomAttrib> _asmCustomAttribs = new(), _modCustomAttribs = new();

    internal ModuleDef(ModuleResolver resolver)
    {
        Resolver = resolver;
    }

    public TypeDef? FindType(string? ns, string name, bool includeExports = true, [DoesNotReturnIf(true)] bool throwIfNotFound = false)
    {
        var type = _typeDefs.Find(ns, name);

        if (type == null && includeExports) {
            type = _exportedTypes.Find(ns, name);
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
        if (FindType(ns, name) != null) {
            throw new InvalidOperationException("A type with the same name already exists");
        }
        var type = new TypeDef(
            this, ns, name, attrs, genericParams,
            baseType ?? Resolver.SysTypes.Object
        );
        _typeDefs.Add(type);
        return type;
    }

    public IEnumerable<MethodDef> MethodDefs()
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

    public override void Print(PrintContext ctx)
        => ctx.Print(AsmName.ToString());

    internal class TypeList : IReadOnlyCollection<TypeDef>
    {
        readonly Dictionary<(string? Ns, string Name), TypeDef> _roots = new();
        readonly List<TypeDef> _nested = new();

        public int Count => _roots.Count + _nested.Count;

        public void Add(TypeDef type)
        {
            if (type.IsNested) {
                _nested.Add(type);
            } else {
                _roots.Add((type.Namespace, type.Name), type);
            }
        }
        
        public TypeDef? Find(string? ns, string name)
            => _roots.GetValueOrDefault((ns, name));

        public IEnumerator<TypeDef> GetEnumerator() => _roots.Values.Concat(_nested).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}