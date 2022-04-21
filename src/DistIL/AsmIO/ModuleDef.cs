namespace DistIL.AsmIO;

using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DistIL.IR;

public class ModuleDef : EntityDef
{
    public PEReader PE { get; }
    public MetadataReader Reader { get; }
    public TypeProvider TypeProvider { get; }
    public ModuleResolver Resolver { get; }

    public AssemblyName AsmName { get; }

    private TypeDef?[] _typeDefs;

    private MethodDef?[] _methodDefs;
    private FieldDef?[] _fieldDefs;
    //resolved reference cache
    private ModuleDef?[] _resolvedAsmRefs;
    private TypeDef?[] _resolvedTypeRefs;
    private MemberDef?[] _resolvedMemberRefs;

    private ExportedType?[] _exportedTypes;

    private Dictionary<TypeDef, ModuleDef> _typeRefRoots = new(); //root assemblies for references of forwarded types
    private Dictionary<(AssemblyReferenceHandle, string? Ns, string Name), TypeDef> _importCache = new();

    ModuleDef EntityDef.Module => this;
    EntityHandle EntityDef.Handle => default;

    public ModuleDef(PEReader pe, ModuleResolver resolver)
    {
        PE = pe;
        Reader = pe.GetMetadataReader();
        TypeProvider = new TypeProvider(this);
        Resolver = resolver;

        AsmName = Reader.GetAssemblyDefinition().GetAssemblyName();

        _typeDefs = new TypeDef[Reader.TypeDefinitions.Count];
        _methodDefs = new MethodDef[Reader.MethodDefinitions.Count];
        _fieldDefs = new FieldDef[Reader.FieldDefinitions.Count];

        _resolvedAsmRefs = new ModuleDef[Reader.AssemblyReferences.Count];
        _resolvedTypeRefs = new TypeDef[Reader.TypeReferences.Count];
        _resolvedMemberRefs = new MemberDef[Reader.MemberReferences.Count];

        _exportedTypes = new ExportedType[Reader.ExportedTypes.Count];
    }

    public RType GetType(EntityHandle handle)
    {
        return handle.Kind switch {
            HandleKind.TypeDefinition => GetType((TypeDefinitionHandle)handle),
            HandleKind.TypeReference => ResolveType((TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => GetTypeSpec((TypeSpecificationHandle)handle),
            _ => throw new NotSupportedException()
        };
    }

    public TypeDef GetType(TypeDefinitionHandle handle)
    {
        return GetEntity(_typeDefs, handle) ??= new TypeDef(this, handle);
    }

    private RType GetTypeSpec(TypeSpecificationHandle handle)
    {
        //TODO: SpecializedType cache
        var info = Reader.GetTypeSpecification(handle);
        return info.DecodeSignature(TypeProvider, default);
    }

    public MethodDef GetMethod(EntityHandle handle)
    {
        return handle.Kind switch {
            HandleKind.MethodDefinition => GetEntity(_methodDefs, handle) ??= new MethodDef(this, (MethodDefinitionHandle)handle),
            HandleKind.MemberReference => ResolveMethod((MemberReferenceHandle)handle),
            _ => throw new NotSupportedException()
        };
    }

    public FieldDef GetField(EntityHandle handle)
    {
        return handle.Kind switch {
            HandleKind.FieldDefinition => GetEntity(_fieldDefs, handle) ??= new FieldDef(this, (FieldDefinitionHandle)handle),
            HandleKind.MemberReference => ResolveField((MemberReferenceHandle)handle),
            _ => throw new NotSupportedException()
        };
    }

    private ModuleDef ResolveAsm(AssemblyReferenceHandle handle)
    {
        ref var entity = ref GetEntity(_resolvedAsmRefs, handle);
        if (entity != null) {
            return entity;
        }
        var info = Reader.GetAssemblyReference(handle);
        return entity = Resolver.Resolve(info.GetAssemblyName());
    }

    private TypeDef ResolveType(TypeReferenceHandle handle)
    {
        ref var entity = ref GetEntity(_resolvedTypeRefs, handle);
        if (entity != null) {
            return entity;
        }
        var info = Reader.GetTypeReference(handle);
        string typeName = Reader.GetString(info.Name);
        string? typeNs = Reader.GetOptString(info.Namespace);
        var scope = (AssemblyReferenceHandle)info.ResolutionScope;
        entity = ResolveType(scope, typeNs, typeName);
        
        if (entity != null) {
            return entity;
        }
        throw new InvalidOperationException($"Could not resolve referenced type '{typeName}'");
    }

    private MethodDef ResolveMethod(MemberReferenceHandle handle)
    {
        ref var entity = ref GetEntity(_resolvedMemberRefs, handle);
        if (entity != null) {
            return (MethodDef)entity;
        }
        var info = Reader.GetMemberReference(handle);
        var parent = ResolveType((TypeReferenceHandle)info.Parent);

        string name = Reader.GetString(info.Name);
        var signature = info.DecodeMethodSignature(TypeProvider, default);

        foreach (var method in parent.Methods) {
            if (method.Name == name && ArgTypesEqual(method, signature.ParameterTypes)) {
                entity = method;
                return method;
            }
            //TODO: check base types
        }
        throw new InvalidOperationException($"Could not resolve referenced method '{parent}::{name}'");
    }

    private bool ArgTypesEqual(MethodDef method, ImmutableArray<RType> types)
    {
        var args1 = method.ArgTypes.AsSpan();
        if (method.IsInstance) {
            args1 = args1.Slice(1); //exclude this
        }
        return args1.SequenceEqual(types.AsSpan());
    }

    private FieldDef ResolveField(MemberReferenceHandle handle)
    {
        ref var entity = ref GetEntity(_resolvedMemberRefs, handle);
        if (entity != null) {
            return (FieldDef)entity;
        }
        var info = Reader.GetMemberReference(handle);
        var parent = ResolveType((TypeReferenceHandle)info.Parent);

        string name = Reader.GetString(info.Name);
        var type = info.DecodeFieldSignature(TypeProvider, default);

        foreach (var field in parent.Fields) {
            if (field.Name == name && field.Type == type) {
                entity = field;
                return field;
            }
        }
        throw new InvalidOperationException($"Could not resolve referenced field '{parent}::{name}'");
    }

    private ExportedType GetExportedType(ExportedTypeHandle handle)
    {
        ref var entity = ref GetEntity(_exportedTypes, handle);
        if (entity != null) {
            return entity;
        }
        var info = Reader.GetExportedType(handle);
        string name = Reader.GetString(info.Name);
        string? ns = Reader.GetOptString(info.Namespace);
        var impl = default(TypeDef);
        var scope = default(EntityDef);

        if (info.Implementation.Kind == HandleKind.ExportedType) {
            var parent = GetExportedType((ExportedTypeHandle)info.Implementation).Implementation;
            impl = parent.GetNestedType(name);
            scope = parent;
        } else {
            var asm = ResolveAsm((AssemblyReferenceHandle)info.Implementation);
            impl = asm.FindType(ns, name);
            scope = asm;
        }
        impl = impl ?? throw new InvalidOperationException("Could not find forwarded type");
        return entity = new ExportedType(this, handle, scope, impl);
    }

    private TypeDef? ResolveType(AssemblyReferenceHandle scopeHandle, string? ns, string name)
    {
        ref var type = ref _importCache.GetOrAddRef((scopeHandle, ns, name));
        if (type != null) {
            return type;
        }
        var scope = ResolveAsm(scopeHandle);
        type = scope.FindType(ns, name);
        if (type != null) {
            SetRefAssembly(type, scope);
        }
        return type;
    }

    private TypeDef? FindType(string? ns, string name)
    {
        foreach (var type in GetTypes()) {
            if (type.Name == name && type.Namespace == ns) {
                return type;
            }
        }
        return null;
    }

    private static ref T? GetEntity<T>(T?[] arr, EntityHandle handle)
    {
        return ref arr[MetadataTokens.GetRowNumber(handle) - 1];
    }
    private static T GetEntity<T>(T?[] arr, EntityHandle handle, Func<T> factory)
    {
        return GetEntity(arr, handle) ??= factory();
    }

    public RType Import(Type type)
    {
        //TODO: add new references
        return FindReferencedType(type) ?? throw new NotImplementedException();
    }

    private TypeDef? FindReferencedType(Type type)
    {
        var queryName = type.Assembly.GetName();
        foreach (var mod in GetReferencedAssemblies()) {
            if (mod.AsmName.Name == queryName.Name) {
                return mod.FindType(type.Namespace, type.Name);
            }
        }
        return null;
    }

    public IEnumerable<TypeDef> GetDefinedTypes()
    {
        foreach (var handle in Reader.TypeDefinitions) {
            yield return GetType(handle);
        }
    }

    public IEnumerable<MethodDef> GetDefinedMethods()
    {
        foreach (var handle in Reader.MethodDefinitions) {
            yield return GetMethod(handle);
        }
    }

    public IEnumerable<TypeDef> GetTypes()
    {
        foreach (var handle in Reader.TypeDefinitions) {
            yield return GetType(handle);
        }
        foreach (var handle in Reader.ExportedTypes) {
            yield return GetExportedType(handle).Implementation;
        }
    }

    public IEnumerable<ModuleDef> GetReferencedAssemblies()
    {
        foreach (var asmHandle in Reader.AssemblyReferences) {
            yield return ResolveAsm(asmHandle);
        }
    }

    public MethodDef? GetEntryPoint()
    {
        int entryPointToken = PE.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress ?? 0;
        if (entryPointToken == 0) {
            return null;
        }
        return GetMethod(MetadataTokens.EntityHandle(entryPointToken));
    }


    private void SetRefAssembly(TypeDef type, ModuleDef root)
    {
        if (type.Module != root) {
            _typeRefRoots.Add(type, root);
        }
    }

    /// <summary> Returns the root referenced assembly in which `type` can be found. </summary>
    public ModuleDef GetRefAssembly(TypeDef type)
    {
        return _typeRefRoots.GetValueOrDefault(type, type.Module);
    }

    public void Save(Stream stream)
    {
        var builder = new BlobBuilder();
        new ModuleWriter(this).Emit(builder);
        builder.WriteContentTo(stream);
    }

    public override string ToString() => AsmName.ToString();
}