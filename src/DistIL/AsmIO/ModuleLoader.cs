namespace DistIL.AsmIO;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal class ModuleLoader
{
    public readonly PEReader _pe;
    public readonly MetadataReader _reader;
    public readonly ModuleResolver _resolver;
    public readonly ModuleDef _mod;
    private readonly EntityList _entities;

    public ModuleLoader(PEReader pe, ModuleResolver resolver, ModuleDef mod)
    {
        _pe = pe;
        _reader = pe.GetMetadataReader();
        _resolver = resolver;
        _mod = mod;
        _entities = new EntityList(_reader);

        var asmDef = _reader.GetAssemblyDefinition();
        _mod.AsmName = asmDef.GetAssemblyName();
        _mod.AsmFlags = asmDef.Flags;
        
        var modDef = _reader.GetModuleDefinition();
        _mod.Name = _reader.GetString(modDef.Name);
    }

    public void Load()
    {
        //Modules are loaded via several passes over the unstructured data provided by SRM.
        //First we create all entities with as much data as possible (so that dependents can refer them),
        //then we progressively fill in remaining properties.
        CreateTypes();
        LoadTypes();

        FillCustomAttribs(_mod, _reader.GetAssemblyDefinition().GetCustomAttributes());
        FillCustomAttribs(_mod, _reader.GetModuleDefinition().GetCustomAttributes(), CustomAttribLink.Type.Module, 0);
    }

    private void CreateTypes()
    {
        _entities.Create<AssemblyReference>(info => _resolver.Resolve(info.GetAssemblyName(), throwIfNotFound: true));
        _entities.Create<TypeReference>(ResolveTypeRef);
        _entities.Create<TypeDefinition>(info => TypeDef.Decode(this, info));
        _entities.Create<TypeSpecification>(info => {
            //Generic constraints may reference modified types in the TypeSpec table.
            //Most GetEntity() consumers cast its result, and will crash if this case is not explicitly handled.
            var sig = new SignatureDecoder(this, info.Signature).DecodeTypeSig();
            return !sig.HasCustomMods ? sig.Type : new ModifiedTypeSpecTableWrapper_() { Sig = sig };
        });

        foreach (var handle in _reader.ExportedTypes) {
            _mod.ExportedTypes.Add(ResolveExportedType(handle));
        }
    }
    private void LoadTypes()
    {
        //Load props like BaseType/Kind (CreateMethod() depends on IsValueType)
        _entities.Iterate((TypeDef entity, TypeDefinition info) => entity.Load1(this, info));

        _entities.Create<FieldDefinition>(info => FieldDef.Decode(this, info));
        _entities.Create<MethodDefinition>(info => MethodDef.Decode(this, info));

        //Populate types with essential data (fields, methods and kind)
        _entities.Iterate((TypeDef entity, TypeDefinition info) => entity.Load2(this, info));

        //Resolve MemberRefs (they depend on type members)
        _entities.Create<MemberReference>(info => {
            return info.GetKind() switch {
                MemberReferenceKind.Field => ResolveField(info),
                MemberReferenceKind.Method => ResolveMethod(info)
            };
        });

        //Create MethodSpecs (they may reference MemberRefs)
        _entities.Create<MethodSpecification>(info => {
            var method = (MethodDefOrSpec)GetEntity(info.Method);
            var decoder = new SignatureDecoder(this, info.Signature, new GenericContext(method));
            Ensure.That(decoder.Reader.ReadSignatureHeader().Kind == SignatureKind.MethodSpecification);
            return new MethodSpec(method.DeclaringType, method.Definition, decoder.DecodeGenArgs());
        });

        //Populate entities with non-essential data (body, custom attrs, etc)
        _entities.Iterate((TypeDef entity, TypeDefinition info) => entity.Load3(this, info));
        _entities.Iterate((FieldDef entity, FieldDefinition info) => entity.Load3(this, info));
        _entities.Iterate((MethodDef entity, MethodDefinition info) => entity.Load3(this, info));

        int entryPointToken = _pe.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress ?? 0;
        if (entryPointToken != 0) {
            var handle = MetadataTokens.EntityHandle(entryPointToken);
            _mod.EntryPoint = (MethodDef)GetEntity(handle);
        }
    }

    public ImmutableArray<TypeDesc> CreateGenericParams(GenericParameterHandleCollection handleList, bool isForMethod)
    {
        if (handleList.Count == 0) {
            return ImmutableArray<TypeDesc>.Empty;
        }
        var builder = ImmutableArray.CreateBuilder<TypeDesc>(handleList.Count);
        foreach (var handle in handleList) {
            var info = _reader.GetGenericParameter(handle);
            string name = _reader.GetString(info.Name);
            builder.Add(new GenericParamType(info.Index, isForMethod, name, info.Attributes));
        }
        return builder.MoveToImmutable();
    }

    private TypeDef ResolveTypeRef(TypeReference info)
    {
        string? ns = _reader.GetOptString(info.Namespace);
        string name = _reader.GetString(info.Name);
        var scope = GetEntity(info.ResolutionScope);
        TypeDef? type = null;

        if (scope is ModuleDef mod) {
            type = mod.FindType(ns, name);

            if (type != null && type.Module != mod) {
                _mod._typeRefRoots[type] = (ModuleDef)scope;
            }
        } else if (scope is TypeDef parent) {
            type = parent.GetNestedType(name);
        }
        return type ?? throw new InvalidOperationException($"Could not resolve referenced type '{ns}.{name}'");
    }
    private TypeDef ResolveExportedType(ExportedTypeHandle handle)
    {
        var info = _reader.GetExportedType(handle);
        string name = _reader.GetString(info.Name);
        string? ns = _reader.GetOptString(info.Namespace);
        TypeDef? impl;

        if (info.Implementation.Kind == HandleKind.ExportedType) {
            var parent = ResolveExportedType((ExportedTypeHandle)info.Implementation);
            impl = parent.GetNestedType(name);
        } else {
            var asm = (ModuleDef)GetEntity(info.Implementation);
            impl = asm.FindType(ns, name) 
                ?? throw new NotImplementedException(); //FIXME: resolve recursive type exports
        }
        return impl ?? throw new InvalidOperationException($"Could not resolve forwarded type '{ns}.{name}'");
    }

    private MethodDesc ResolveMethod(MemberReference info)
    {
        var rootParent = (TypeDesc)GetEntity(info.Parent);
        string name = _reader.GetString(info.Name);
        var signature = new SignatureDecoder(this, info.Signature).DecodeMethodSig();
        var spec = GenericContext.Empty;

        for (var parent = rootParent; parent != null; parent = parent.BaseType) {
            var method = parent.FindMethod(name, signature, spec);
            if (method != null) {
                return method;
            }
            if (parent.BaseType is TypeSpec) {
                //TODO: ResolveMethod() for generic type inheritance
                throw new NotImplementedException();
            }
        }
        throw new InvalidOperationException($"Could not resolve referenced method '{rootParent}::{name}'");
    }
    private FieldDesc ResolveField(MemberReference info)
    {
        var rootParent = (TypeDesc)GetEntity(info.Parent);
        string name = _reader.GetString(info.Name);

        for (var parent = rootParent; parent != null; parent = parent.BaseType) {
            var field = parent.FindField(name);
            if (field != null) {
                return field;
            }
        }
        throw new InvalidOperationException($"Could not resolve referenced field '{rootParent}::{name}'");
    }

    public void FillGenericParams(ModuleEntity parent, ImmutableArray<TypeDesc> genPars, GenericParameterHandleCollection handleList)
    {
        foreach (var handle in handleList) {
            var info = _reader.GetGenericParameter(handle);
            var param = (GenericParamType)genPars[info.Index];
            param.Load3(this, info);
            FillCustomAttribs(parent, info.GetCustomAttributes(), CustomAttribLink.Type.GenericParam, info.Index);
        }
    }

    public void FillCustomAttribs(ModuleEntity entity, CustomAttributeHandleCollection handles, CustomAttribLink.Type linkType = default, int linkIndex = 0)
    {
        if (handles.Count > 0) {
            _mod._customAttribs.Add(new(entity, linkIndex, linkType), DecodeCustomAttribs(handles));
        }
    }

    public CustomAttrib[] DecodeCustomAttribs(CustomAttributeHandleCollection handleList)
    {
        var attribs = new CustomAttrib[handleList.Count];
        int index = 0;

        foreach (var handle in handleList) {
            var attrib = _reader.GetCustomAttribute(handle);
            var ctor = (MethodDesc)GetEntity(attrib.Constructor);
            var blob = _reader.GetBlobBytes(attrib.Value);
            attribs[index++] = new CustomAttrib(ctor, blob, _mod);
        }
        return attribs;
    }

    public FuncPtrType DecodeMethodSig(StandaloneSignatureHandle handle)
    {
        var info = _reader.GetStandaloneSignature(handle);
        Ensure.That(info.GetCustomAttributes().Count == 0);
        
        return new FuncPtrType(new SignatureDecoder(this, info.Signature).DecodeMethodSig());
    }

    public Entity GetEntity(EntityHandle handle) => _entities.Get(handle);
    public TypeDef GetType(TypeDefinitionHandle handle) => (TypeDef)GetEntity(handle);
    public FieldDef GetField(FieldDefinitionHandle handle) => (FieldDef)GetEntity(handle);
    public MethodDef GetMethod(MethodDefinitionHandle handle) => (MethodDef)GetEntity(handle);

    class EntityList
    {
        static readonly TableIndex[] s_Tables = {
            TableIndex.AssemblyRef,
            TableIndex.TypeRef, TableIndex.MemberRef,
            TableIndex.TypeDef, TableIndex.TypeSpec,
            TableIndex.Field,
            TableIndex.MethodDef, TableIndex.MethodSpec
        };
        readonly MetadataReader _reader;
        readonly Entity[] _entities; //entities laid out linearly
        readonly (int Start, int End)[] _ranges; //inclusive range in _entities for each TableIndex
        int _index; //current index in _entities for Create()

        public EntityList(MetadataReader reader)
        {
            _reader = reader;
            _entities = new Entity[s_Tables.Sum(reader.GetTableRowCount)];
            _ranges = new (int, int)[s_Tables.Max(v => (int)v) + 1];
        }

        public void Create<TInfo>(Func<TInfo, Entity> factory)
        {
            var table = GetTable<TInfo>();
            int numRows = _reader.GetTableRowCount(table);

            var (start, end) = _ranges[(int)table] = (_index, _index + numRows);
            _index = end;

            for (int i = 0; i < numRows; i++) {
                _entities[start + i] = factory(GetInfo<TInfo>(_reader, i + 1));
            }
        }
        public Entity Get(EntityHandle handle)
        {
            var tableIdx = (int)handle.Kind;
            Debug.Assert(Array.IndexOf(s_Tables, (TableIndex)tableIdx) >= 0);

            var (start, end) = _ranges[tableIdx];
            int index = start + MetadataTokens.GetRowNumber(handle) - 1; //rows are 1 based
            Ensure.That(index < end);

            //FIXME: Create() will update the ranges before populating the table,
            //so that ResolveTypeRef() can call Get() with a nested type.
            //There must be a better way to do that...
            return _entities[index] 
                ?? throw new InvalidOperationException("Table entry is not yet populated");
        }
        public void Iterate<TEntity, TInfo>(Action<TEntity, TInfo> cb)
        {
            var table = GetTable<TInfo>();
            Debug.Assert(Array.IndexOf(s_Tables, table) >= 0);

            var (start, end) = _ranges[(int)table];
            for (int i = 0; i < end - start; i++) {
                var entity = (TEntity)(object)_entities[start + i];
                var info = GetInfo<TInfo>(_reader, i + 1);
                cb(entity, info);
            }
        }

        private static TInfo GetInfo<TInfo>(MetadataReader reader, int rowId)
        {
            if (typeof(TInfo) == typeof(AssemblyReference)) {
                return (TInfo)(object)reader.GetAssemblyReference(MetadataTokens.AssemblyReferenceHandle(rowId));
            }
            if (typeof(TInfo) == typeof(TypeReference)) {
                return (TInfo)(object)reader.GetTypeReference(MetadataTokens.TypeReferenceHandle(rowId));
            }
            if (typeof(TInfo) == typeof(TypeDefinition)) {
                return (TInfo)(object)reader.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(rowId));
            }
            if (typeof(TInfo) == typeof(TypeSpecification)) {
                return (TInfo)(object)reader.GetTypeSpecification(MetadataTokens.TypeSpecificationHandle(rowId));
            }
            if (typeof(TInfo) == typeof(FieldDefinition)) {
                return (TInfo)(object)reader.GetFieldDefinition(MetadataTokens.FieldDefinitionHandle(rowId));
            }
            if (typeof(TInfo) == typeof(MethodDefinition)) {
                return (TInfo)(object)reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(rowId));
            }
            if (typeof(TInfo) == typeof(MethodSpecification)) {
                return (TInfo)(object)reader.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(rowId));
            }
            if (typeof(TInfo) == typeof(MemberReference)) {
                return (TInfo)(object)reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(rowId));
            }
            throw new NotImplementedException();
        }
        private static TableIndex GetTable<TInfo>()
        {
            if (typeof(TInfo) == typeof(AssemblyReference)) {
                return TableIndex.AssemblyRef;
            }
            if (typeof(TInfo) == typeof(TypeReference)) {
                return TableIndex.TypeRef;
            }
            if (typeof(TInfo) == typeof(TypeDefinition)) {
                return TableIndex.TypeDef;
            }
            if (typeof(TInfo) == typeof(TypeSpecification)) {
                return TableIndex.TypeSpec;
            }
            if (typeof(TInfo) == typeof(FieldDefinition)) {
                return TableIndex.Field;
            }
            if (typeof(TInfo) == typeof(MethodDefinition)) {
                return TableIndex.MethodDef;
            }
            if (typeof(TInfo) == typeof(MethodSpecification)) {
                return TableIndex.MethodSpec;
            }
            if (typeof(TInfo) == typeof(MemberReference)) {
                return TableIndex.MemberRef;
            }
            throw new NotImplementedException();
        }
    }
}

internal class ModifiedTypeSpecTableWrapper_ : Entity
{
    public TypeSig Sig = null!;
    public string Name => "";
}