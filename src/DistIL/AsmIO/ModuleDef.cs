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
    public SignatureTypeDecoder TypeDecoder { get; }
    public ModuleResolver Resolver { get; }

    public AssemblyName Name { get; }

    private TypeDef?[] _typeDefs;

    private MethodDef?[] _methodDefs;
    private FieldDef?[] _fieldDefs;
    //resolved reference cache
    private ModuleDef?[] _resolvedAsmRefs;
    private TypeDef?[] _resolvedTypeRefs;
    private MemberDef?[] _resolvedMemberRefs;

    ModuleDef EntityDef.Module => this;

    public ModuleDef(PEReader pe, ModuleResolver resolver)
    {
        PE = pe;
        Reader = pe.GetMetadataReader();
        TypeDecoder = new SignatureTypeDecoder(this);
        Resolver = resolver;

        Name = Reader.GetAssemblyDefinition().GetAssemblyName();

        _typeDefs = new TypeDef[Reader.TypeDefinitions.Count];
        _methodDefs = new MethodDef[Reader.MethodDefinitions.Count];
        _fieldDefs = new FieldDef[Reader.FieldDefinitions.Count];

        _resolvedAsmRefs = new ModuleDef[Reader.AssemblyReferences.Count];
        _resolvedTypeRefs = new TypeDef[Reader.TypeReferences.Count];
        _resolvedMemberRefs = new MemberDef[Reader.MemberReferences.Count];
    }

    public TypeDef GetType(EntityHandle handle)
    {
        return handle.Kind switch {
            HandleKind.TypeDefinition => GetEntity(_typeDefs, handle) ??= new TypeDef(this, (TypeDefinitionHandle)handle),
            HandleKind.TypeReference => ResolveType((TypeReferenceHandle)handle),
            _ => throw new NotSupportedException()
        };
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
        ref var resolved = ref GetEntity(_resolvedAsmRefs, handle);
        if (resolved != null) {
            return resolved;
        }
        var entity = Reader.GetAssemblyReference(handle);
        return resolved = Resolver.Resolve(entity.GetAssemblyName());
    }

    private TypeDef ResolveType(TypeReferenceHandle handle)
    {
        ref var resolved = ref GetEntity(_resolvedTypeRefs, handle);
        if (resolved != null) {
            return resolved;
        }
        var entity = Reader.GetTypeReference(handle);
        var typeName = Reader.GetString(entity.Name);
        var typeNs = Reader.GetString(entity.Namespace);
        
        var scope = ResolveAsm((AssemblyReferenceHandle)entity.ResolutionScope);
        resolved = scope.ResolveType(typeNs, typeName);
        if (resolved != null) {
            return resolved;
        }
        //were are you hiding little shit?
        throw new InvalidOperationException($"Could not resolve referenced type '{typeName}'");
    }

    private MethodDef ResolveMethod(MemberReferenceHandle handle)
    {
        ref var resolved = ref GetEntity(_resolvedMemberRefs, handle);
        if (resolved != null) {
            return (MethodDef)resolved;
        }
        var entity = Reader.GetMemberReference(handle);
        var parent = ResolveType((TypeReferenceHandle)entity.Parent);

        string name = Reader.GetString(entity.Name);
        var signature = entity.DecodeMethodSignature(TypeDecoder, null);

        foreach (var method in parent.Methods) {
            if (method.Name == name && method.ArgTypes.SequenceEqual(signature.ParameterTypes)) {
                resolved = method;
                return method;
            }
            //TODO: check base types
        }
        throw new InvalidOperationException($"Could not resolve referenced method '{parent}::{name}'");
    }

    private FieldDef ResolveField(MemberReferenceHandle handle)
    {
        ref var resolved = ref GetEntity(_resolvedMemberRefs, handle);
        if (resolved != null) {
            return (FieldDef)resolved;
        }
        var entity = Reader.GetMemberReference(handle);
        var parent = ResolveType((TypeReferenceHandle)entity.Parent);

        string name = Reader.GetString(entity.Name);
        var type = entity.DecodeFieldSignature(TypeDecoder, null);

        foreach (var field in parent.Fields) {
            if (field.Name == name && field.Type == type) {
                resolved = field;
                return field;
            }
        }
        throw new InvalidOperationException($"Could not resolve referenced field '{parent}::{name}'");
    }

    private TypeDef? ResolveType(string? ns, string name)
    {
        //https://github.com/dotnet/runtime/blob/5707668eac999163740e42c70f228772e6bc3680/src/coreclr/tools/Common/TypeSystem/Ecma/EcmaModule.cs#L287
        var scope = this;

        for (int i = 0; i < 64; i++) {
            var reader = scope.Reader;

            foreach (var type in scope.GetDefinedTypes()) {
                if (type.Name == name && type.Namespace == ns) {
                    return type;
                }
            }
            foreach (var expTypeHandle in reader.ExportedTypes) {
                var expType = reader.GetExportedType(expTypeHandle);
                string typeName = reader.GetString(expType.Name);
                string typeNs = reader.GetString(expType.Namespace);

                if (typeName == name && typeNs == ns) {
                    if (expType.IsForwarder) {
                        scope = scope.ResolveAsm((AssemblyReferenceHandle)expType.Implementation);
                        goto NextModuleInForwardChain;
                    } else {
                        throw new NotImplementedException();
                    }
                }
            }
            return null;
        NextModuleInForwardChain:;
        }
        throw new InvalidOperationException("Loop detected in type forward chain of assembly " + Name.Name);
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
        //Try to find in the referenced assemblies
        var defininingAsm = type.Assembly.GetName();
        foreach (var asmRefHandle in Reader.AssemblyReferences) {
            var asmRef = Reader.GetAssemblyReference(asmRefHandle);
            var module = ResolveAsm(asmRefHandle);
            var resolvedType = module.ResolveType(type.Namespace, type.Name);
            if (resolvedType != null) {
                return resolvedType;
            }
        }
        throw new NotImplementedException();
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
            var expType = Reader.GetExportedType(handle);
            string name = Reader.GetString(expType.Name);
            string ns = Reader.GetString(expType.Namespace);

            if (expType.IsForwarder) {
                var scope = ResolveAsm((AssemblyReferenceHandle)expType.Implementation);
                yield return scope.ResolveType(ns, name) ?? throw new InvalidOperationException();
            } else {
                throw new NotImplementedException();
            }
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

    public void Save(Stream stream)
    {
        var builder = new BlobBuilder();
        new ModuleWriter(this).Emit(builder);
        foreach (var blob in builder.GetBlobs()) {
            stream.Write(blob.GetBytes());
        }
    }

    public override string ToString() => Name.ToString();
}