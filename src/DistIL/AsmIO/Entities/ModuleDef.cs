namespace DistIL.AsmIO;

using System.Collections;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;

public class ModuleDef : EntityDesc
{
    public string ModName { get; set; } = null!;
    public AssemblyName AsmName { get; set; } = null!;

    public MethodDef? EntryPoint { get; set; }

    /// <summary> All types defined in this module (incl. nested). </summary>
    public IReadOnlyCollection<TypeDef> TypeDefs => _typeDefs;

    /// <summary> All types exported by this module (incl. nested). </summary>
    public IReadOnlyCollection<TypeDef> ExportedTypes => _exportedTypes;

    public ModuleResolver Resolver { get; }

    internal TypeList _typeDefs = new(), _exportedTypes = new();

    internal Dictionary<TypeDef, ModuleDef> _typeRefRoots = new(); // root assemblies for references of forwarded types
    internal List<CustomAttrib> _asmCustomAttribs = new(), _modCustomAttribs = new();

    internal ModuleLoader? _loader;
    private DebugSymbolStore? _debugSymbols;
    private bool _triedToLoadDebugSymbols = false;

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
        GenericParamType[]? genericParams = null)
    {
        if (FindType(ns, name) != null) {
            throw new InvalidOperationException("A type with the same name already exists");
        }
        var type = new TypeDef(
            this, ns, name, attrs, genericParams ?? [],
            baseType ?? Resolver.SysTypes.Object
        );
        _typeDefs.Add(type);
        return type;
    }

    public IEnumerable<MethodDef> MethodDefs()
        => TypeDefs.SelectMany(t => t.Methods);

    public List<CustomAttrib> GetCustomAttribs(bool forAssembly)
        => forAssembly ? _asmCustomAttribs : _modCustomAttribs;

    /// <summary> Serializes this module to the specified stream. </summary>
    /// <param name="pdbStream">
    /// If non-null, specifies the stream where the PDB data should be serialized into.
    /// If the module does not have debug symbols, nothing will be written to this stream.
    /// </param>
    public void Save(Stream stream, Stream? pdbStream, string path)
    {
        var writer = new ModuleWriter(this);
        writer.BuildTables();
        writer.Serialize(stream, pdbStream, path);
    }
    public void Save(string path, bool savePdb)
    {
        using var stream = File.Create(path);

        using var pdbStream = savePdb && _debugSymbols != null
            ? File.Create(Path.ChangeExtension(path, ".pdb")): null;

        Save(stream, pdbStream, path);
    }

    public DebugSymbolStore? GetDebugSymbols(bool create = false, Func<string, Stream?>? pdbFileStreamProvider = null)
    {
        if (_debugSymbols != null) {
            return _debugSymbols;
        }

        if (_loader != null && !_triedToLoadDebugSymbols) {
            _triedToLoadDebugSymbols = true;

            try {
                _loader._pe.TryOpenAssociatedPortablePdb(_loader._path, OpenPdbStream, out var provider, out string? pdbPath);

                if (provider != null) {
                    _debugSymbols = new PortablePdbSymbolStore(this, provider.GetMetadataReader());
                }
            } catch (Exception ex) {
                Resolver._logger?.Error($"Failed to load debug symbols for module '{ModName}'", ex);
            }
        }

        if (_debugSymbols == null && create) {
            _debugSymbols = new DebugSymbolStore(this);
        }
        return _debugSymbols;

        Stream? OpenPdbStream(string name)
        {
            try {
                using var stream = pdbFileStreamProvider?.Invoke(name) ?? File.OpenRead(name);

                // Read file to memory to avoid locking it and allow for overwrites in Save().
                // This isn't quite efficient and will probably result in two copies of the file
                // in memory (one internal to MetadataReader/MemoryBlock).
                //
                // In the future, we could avoid this by replicating TryOpenAssociatedPortablePdb() and
                // directly calling FromPortablePdbStream() with the Prefetch flag.
                var ms = new MemoryStream((int)stream.Length);
                stream.CopyTo(ms);
                ms.Position = 0;
                return ms;
            } catch (FileNotFoundException) {
                // nop
            }
            return null;
        }
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