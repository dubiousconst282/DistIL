namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal class ModuleLoader
{
    public readonly PEReader _pe;
    public readonly MetadataReader _reader;
    private readonly ModuleResolver _resolver;
    public readonly TypeProvider _typeProvider;
    public readonly ModuleDef _mod;
    private Entity[] _entities; //entities laid out linearly
    private (int Start, int End)[] _entityRanges; //inclusive range in _entities for each TableIndex
    private int _entityIdx; //current index in _entities for Add()

    public ModuleLoader(PEReader pe, ModuleResolver resolver, ModuleDef mod)
    {
        _pe = pe;
        _reader = pe.GetMetadataReader();
        _resolver = resolver;
        _typeProvider = new TypeProvider(this);
        _mod = mod;

        int numEntities = 0;
        foreach (var table in s_EntityTables) {
            numEntities += _reader.GetTableRowCount(table);
        }
        _entities = new Entity[numEntities];
        _entityRanges = new (int, int)[64];

        var asmDef = _reader.GetAssemblyDefinition();
        _mod.AsmName = asmDef.GetAssemblyName();
        _mod.AsmFlags = asmDef.Flags;
        
        var modDef = _reader.GetModuleDefinition();
        _mod.Name = _reader.GetString(modDef.Name);
    }

    public void Load()
    {
        //Module loading consists of two passes through the defined types:
        // 1. Create the entities, so they can be referenced while loading others
        // 2. Load remaining metadata (members, base type, ...)
        CreateTypes();
        LoadTypes();
    }

    private void CreateTypes()
    {
        foreach (var handle in _reader.AssemblyReferences) {
            var info = _reader.GetAssemblyReference(handle);
            var entity = _resolver.Resolve(info.GetAssemblyName());
            AddEntity(handle, entity);
            _mod.AssemblyRefs.Add(entity);
        }
        foreach (var handle in _reader.TypeReferences) {
            var info = _reader.GetTypeReference(handle);
            AddEntity(handle, ResolveType(info));
        }
        foreach (var handle in _reader.ExportedTypes) {
            var entity = ResolveExportedType(handle);
            _mod.ExportedTypes.Add(entity);
        }

        foreach (var handle in _reader.TypeDefinitions) {
            var info = _reader.GetTypeDefinition(handle);
            var entity = CreateType(info);
            AddEntity(handle, entity);
            _mod.TypeDefs.Add(entity);
        }
        //We need to load type specs because they may get lookedup by GetEntity()
        int numTypeSpecs = _reader.GetTableRowCount(TableIndex.TypeSpec);
        for (int rowId = 0; rowId < numTypeSpecs; rowId++) {
            var handle = MetadataTokens.TypeSpecificationHandle(rowId + 1);
            var info = _reader.GetTypeSpecification(handle);
            AddEntity(handle, info.DecodeSignature(_typeProvider, default));
        }
    }

    private void LoadTypes()
    {
        //Kind/IsValueType depends on SysTypes
        var coreLib = FindCoreLib();
        _mod.CoreLib = coreLib;
        _mod.SysTypes = coreLib == _mod ? new SystemTypes(coreLib) : coreLib.SysTypes;

        //Load members, and props like BaseType/Kind
        foreach (var handle in _reader.TypeDefinitions) {
            var entity = GetType(handle);
            var info = _reader.GetTypeDefinition(handle);
            entity.Load(this, info);
        }

        foreach (var handle in _reader.FieldDefinitions) {
            var info = _reader.GetFieldDefinition(handle);
            var entity = CreateField(info);
            AddEntity(handle, entity);
            entity.Load(this, info);
        }
        foreach (var handle in _reader.MethodDefinitions) {
            var info = _reader.GetMethodDefinition(handle);
            var entity = CreateMethod(info);
            AddEntity(handle, entity);
        }
        //Following depend on props like BaseType/Kind
        foreach (var handle in _reader.TypeDefinitions) {
            var entity = GetType(handle);
            var info = _reader.GetTypeDefinition(handle);
            entity.Load2(this, info);
        }
        foreach (var handle in _reader.MemberReferences) {
            var info = _reader.GetMemberReference(handle);
            Entity entity = info.GetKind() switch {
                MemberReferenceKind.Field => ResolveField(info),
                MemberReferenceKind.Method => ResolveMethod(info)
            };
            AddEntity(handle, entity);
        }
        int numMethodSpecs = _reader.GetTableRowCount(TableIndex.MethodSpec);
        for (int rowId = 0; rowId < numMethodSpecs; rowId++) {
            var handle = MetadataTokens.MethodSpecificationHandle(rowId + 1);
            var info = _reader.GetMethodSpecification(handle);
            var method = (MethodDefOrSpec)GetEntity(info.Method);
            var genArgs = info.DecodeSignature(_typeProvider, default);
            AddEntity(handle, new MethodSpec(method.DeclaringType, method.Definition, genArgs));
        }
        foreach (var handle in _reader.MethodDefinitions) {
            var info = _reader.GetMethodDefinition(handle);
            var entity = GetMethod(handle);
            entity.Load(this, info);
        }
        var asmDef = _reader.GetAssemblyDefinition();
        _mod.CustomAttribs = DecodeCustomAttribs(asmDef.GetCustomAttributes());

        int entryPointToken = _pe.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress ?? 0;
        if (entryPointToken != 0) {
            var handle = MetadataTokens.EntityHandle(entryPointToken);
            _mod.EntryPoint = (MethodDef)GetEntity(handle);
        }
    }

    private TypeDef CreateType(TypeDefinition info)
    {
        return new TypeDef(
            _mod, _reader.GetOptString(info.Namespace),
            _reader.GetString(info.Name),
            info.Attributes,
            CreatePlaceholderGenericArgs(info.GetGenericParameters(), false)
        );
    }
    private FieldDef CreateField(FieldDefinition info)
    {
        var type = info.DecodeSignature(_typeProvider, default);

        return new FieldDef(
            GetType(info.GetDeclaringType()), 
            type, _reader.GetString(info.Name), 
            info.Attributes,
            _reader.DecodeConst(info.GetDefaultValue()),
            info.GetOffset()
        );
    }
    private MethodDef CreateMethod(MethodDefinition info)
    {
        var declaringType = GetType(info.GetDeclaringType());
        var sig = info.DecodeSignature(_typeProvider, default);
        string name = _reader.GetString(info.Name);

        var attribs = info.Attributes;
        bool isInst = (attribs & MethodAttributes.Static) == 0;
        var pars = ImmutableArray.CreateBuilder<ParamDef>(sig.RequiredParameterCount + (isInst ? 1 : 0));

        if (isInst) {
            var thisType = declaringType as TypeDesc;
            if (thisType.IsValueType) {
                thisType = new ByrefType(thisType);
            }
            pars.Add(new ParamDef(thisType, 0, "this"));
        }
        foreach (var paramType in sig.ParameterTypes) {
            pars.Add(new ParamDef(paramType, pars.Count));
        }
        return new MethodDef(
            declaringType, 
            sig.ReturnType, pars.ToImmutable(),
            name, 
            attribs, info.ImplAttributes,
            genericParams: CreatePlaceholderGenericArgs(info.GetGenericParameters(), true)
        );
    }

    private ModuleDef FindCoreLib()
    {
        foreach (var asm in _mod.AssemblyRefs) {
            var name = asm.AsmName.Name;
            if (name is "System.Runtime" or "System.Private.CoreLib") {
                return asm;
            }
        }
        if (_mod.AsmName.Name == "System.Private.CoreLib") {
            return _mod;
        }
        throw new NotImplementedException();
    }

    private TypeDef ResolveType(TypeReference info)
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
        if (type == null) {
            throw new InvalidOperationException($"Could not resolve referenced type '{ns}.{name}'");
        }
        return type;
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
            impl = asm.FindType(ns, name, includeExports: false) 
                ?? throw new NotImplementedException(); //FIXME: resolve recursive type exports
        }
        return impl ?? throw new InvalidOperationException($"Could not resolve forwarded type '{ns}.{name}'");
    }

    private MethodDesc ResolveMethod(MemberReference info)
    {
        var rootParent = (TypeDesc)GetEntity(info.Parent);
        string name = _reader.GetString(info.Name);
        var signature = new MethodSig(info.DecodeMethodSignature(_typeProvider, default));

        for (var parent = rootParent; parent != null; parent = parent.BaseType) {
            var method = parent.FindMethod(name, signature);
            if (method != null) {
                return method;
            }
        }
        throw new InvalidOperationException($"Could not resolve referenced method '{rootParent}::{name}'");
    }
    private FieldDesc ResolveField(MemberReference info)
    {
        var rootParent = (TypeDesc)GetEntity(info.Parent);
        string name = _reader.GetString(info.Name);
        var type = info.DecodeFieldSignature(_typeProvider, default);

        for (var parent = rootParent; parent != null; parent = parent.BaseType) {
            var field = parent.FindField(name, type);
            if (field != null) {
                return field;
            }
        }
        throw new InvalidOperationException($"Could not resolve referenced field '{rootParent}::{name}'");
    }

    public ImmutableArray<TypeDesc> DecodeGenericParams(GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0) {
            return ImmutableArray<TypeDesc>.Empty;
        }
        var builder = ImmutableArray.CreateBuilder<TypeDesc>(handles.Count);
        foreach (var parHandle in handles) {
            var par = _reader.GetGenericParameter(parHandle);
            bool isMethodParam = par.Parent.Kind is
                HandleKind.MethodDefinition or
                HandleKind.MethodSpecification or
                HandleKind.MemberReference;
            var name = _reader.GetString(par.Name);
            var constraints = ImmutableArray.CreateBuilder<TypeDesc>();
            foreach (var constraintHandle in par.GetConstraints()) {
                var constraint = _reader.GetGenericParameterConstraint(constraintHandle);
                //TODO: constraint.GetCustomAttributes()
                constraints.Add((TypeDesc)GetEntity(constraint.Type));
            }
            builder.Add(new GenericParamType(par.Index, isMethodParam, name, constraints.ToImmutable()));
        }
        return builder.ToImmutable();
    }
    public ImmutableArray<CustomAttrib> DecodeCustomAttribs(CustomAttributeHandleCollection handles)
    {
        var attribs = ImmutableArray.CreateBuilder<CustomAttrib>(handles.Count);
        return attribs.ToImmutable();

        foreach (var handle in handles) {
            var attrib = _reader.GetCustomAttribute(handle);
            var value = attrib.DecodeValue(_typeProvider);

            attribs.Add(new CustomAttrib() {
                Constructor = (MethodDesc)GetEntity(attrib.Constructor),
                FixedArgs = value.FixedArguments.Select(a => new CustomAttribArg() {
                    Type = a.Type,
                    Value = a.Value
                }).ToImmutableArray(),
                NamedArgs = value.NamedArguments.Select(a => new CustomAttribArg() {
                    Type = a.Type,
                    Value = a.Value,
                    Name = a.Name!,
                    Kind = (CustomAttribArgKind)a.Kind
                }).ToImmutableArray()
            });
        }
        return attribs.ToImmutable();
    }
    public FuncPtrType DecodeMethodSig(StandaloneSignatureHandle handle)
    {
        var info = _reader.GetStandaloneSignature(handle);
        var sig = info.DecodeMethodSignature(_typeProvider, default);
        return new FuncPtrType(sig);
    }

    public PropertyDef DecodeProperty(TypeDef parent, PropertyDefinitionHandle handle)
    {
        var info = _reader.GetPropertyDefinition(handle);
        var sig = info.DecodeSignature(_typeProvider, default);
        var accs = info.GetAccessors();
        var otherAccessors = accs.Others.IsEmpty
            ? default(ImmutableArray<MethodDef>)
            : accs.Others.Select(GetMethod).ToImmutableArray();

        return new PropertyDef(
            parent, _reader.GetString(info.Name), new MethodSig(sig),
            accs.Getter.IsNil ? null : GetMethod(accs.Getter),
            accs.Setter.IsNil ? null : GetMethod(accs.Setter),
            otherAccessors,
            _reader.DecodeConst(info.GetDefaultValue()),
            info.Attributes
        );
    }
    public EventDef DecodeEvent(TypeDef parent, EventDefinitionHandle handle)
    {
        var info = _reader.GetEventDefinition(handle);
        var type = (TypeDesc)GetEntity(info.Type);
        var accs = info.GetAccessors();
        var otherAccessors = accs.Others.IsEmpty
            ? default(ImmutableArray<MethodDef>)
            : accs.Others.Select(GetMethod).ToImmutableArray();

        return new EventDef(
            parent, _reader.GetString(info.Name), type,
            accs.Adder.IsNil ? null : GetMethod(accs.Adder),
            accs.Remover.IsNil ? null : GetMethod(accs.Remover),
            accs.Raiser.IsNil ? null : GetMethod(accs.Raiser),
            otherAccessors,
            info.Attributes
        );
    }

    private ImmutableArray<TypeDesc> CreatePlaceholderGenericArgs(GenericParameterHandleCollection pars, bool isForMethod)
    {
        if (pars.Count == 0) {
            return ImmutableArray<TypeDesc>.Empty;
        }
        var builder = ImmutableArray.CreateBuilder<TypeDesc>(pars.Count);
        for (int i = 0; i < pars.Count; i++) {
            builder.Add(new GenericParamType(i, isForMethod));
        }
        return builder.ToImmutable();
    }

    //Adds an entity to the map. This will break if entities of the same type are not added sequentially.
    private void AddEntity(EntityHandle handle, Entity entity)
    {
        var tableIdx = (int)handle.Kind;
        Assert(Array.IndexOf(s_EntityTables, (TableIndex)tableIdx) >= 0);

        _entities[_entityIdx] = entity;
        ref var range = ref _entityRanges[tableIdx];
        if (range.Start == 0) {
            range.Start = _entityIdx + 1;
        }
        range.End = _entityIdx;
        _entityIdx++;
    }
    public Entity GetEntity(EntityHandle handle)
    {
        var tableIdx = (int)handle.Kind;
        Assert(Array.IndexOf(s_EntityTables, (TableIndex)tableIdx) >= 0);

        var (start, end) = _entityRanges[tableIdx];
        int index = start + MetadataTokens.GetRowNumber(handle) - 2; //row ids and start index are 1 based
        Ensure(index <= end);

        if (handle.Kind == HandleKind.TypeDefinition) {
            var info = _reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            var name = _reader.GetString(info.Name);
            Assert(name == ((TypeDef)_entities[index]).Name);
        }
        return _entities[index];
    }
    public TypeDef GetType(TypeDefinitionHandle handle) => (TypeDef)GetEntity(handle);
    public FieldDef GetField(FieldDefinitionHandle handle) => (FieldDef)GetEntity(handle);
    public MethodDef GetMethod(MethodDefinitionHandle handle) => (MethodDef)GetEntity(handle);

    //Entity tables we're interested in
    static TableIndex[] s_EntityTables = {
        TableIndex.AssemblyRef,
        TableIndex.TypeRef, TableIndex.MemberRef,
        TableIndex.TypeDef, TableIndex.TypeSpec,
        TableIndex.Field,
        TableIndex.MethodDef, TableIndex.MethodSpec,
        TableIndex.Property, TableIndex.Event
    };
}