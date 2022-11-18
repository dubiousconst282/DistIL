namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

partial class ModuleWriter
{
    private void AllocHandles()
    {
        int typeIdx = 1, fieldIdx = 1, methodIdx = 1;

        foreach (var type in _mod.TypeDefs) {
            _handleMap.Add(type, MetadataTokens.TypeDefinitionHandle(typeIdx++));

            foreach (var field in type.Fields) {
                _handleMap.Add(field, MetadataTokens.FieldDefinitionHandle(fieldIdx++));
            }
            foreach (var method in type.Methods) {
                _handleMap.Add(method, MetadataTokens.MethodDefinitionHandle(methodIdx++));
            }
        }

        var globalType = _mod.FindType(null, "<Module>");
        Debug.Assert(globalType == null || _mod.TypeDefs[0] == globalType); //Global type row id must be #1
    }

    private EntityHandle CreateHandle(Entity entity)
    {
        switch (entity) {
            case TypeDef type: {
                var scope = (Entity?)type.DeclaringType ?? _mod._typeRefRoots.GetValueOrDefault(type, type.Module);
                return _builder.AddTypeReference(
                    GetHandle(scope),
                    AddString(type.Namespace),
                    AddString(type.Name)
                );
            }
            case TypeDesc type: {
                return _builder.AddTypeSpecification(
                    EncodeSig(b => EncodeType(b.TypeSpecificationSignature(), type))
                );
            }
            case TypeSig sig: {
                return _builder.AddTypeSpecification(
                    EncodeSig(b => EncodeType(b.TypeSpecificationSignature(), sig))
                );
            }
            case MethodDesc method: {
                EntityHandle refHandle;
                var spec = method as MethodSpec;

                if (spec is { DeclaringType: TypeDef }) {
                    refHandle = GetHandle(spec.Definition);
                } else {
                    refHandle = _builder.AddMemberReference(
                        GetHandle(method.DeclaringType),
                        AddString(method.Name),
                        EncodeMethodSig((method as MethodDefOrSpec)?.Definition ?? method)
                    );
                }
                return spec is { IsGeneric: true }
                    ? _builder.AddMethodSpecification(refHandle, EncodeMethodSpecSig(spec))
                    : refHandle;
            }
            case FieldDefOrSpec field: {
                return _builder.AddMemberReference(
                    GetHandle(field.DeclaringType),
                    AddString(field.Name),
                    EncodeFieldSig(field.Definition)
                );
            }
            case ModuleDef module: {
                var name = module.AsmName;
                return _builder.AddAssemblyReference(
                    AddString(name.Name),
                    name.Version!,
                    AddString(name.CultureName),
                    AddBlob(name.GetPublicKey() ?? name.GetPublicKeyToken()),
                    (AssemblyFlags)name.Flags,
                    default
                );
            }
            default: throw new NotImplementedException();
        }
    }

    private EntityHandle GetHandle(Entity entity)
    {
        if (entity is PrimType primType) {
            entity = primType.GetDefinition(_mod.Resolver);
        }
        if (!_handleMap.TryGetValue(entity, out var handle)) {
            _handleMap[entity] = handle = CreateHandle(entity);
        }
        return handle;
    }

    private EntityHandle GetSigHandle(TypeSig sig)
    {
        //Avoid boxing if the sig has no custom mods
        return sig.HasCustomMods ? GetHandle((Entity)sig) : GetHandle(sig.Type);
    }
}