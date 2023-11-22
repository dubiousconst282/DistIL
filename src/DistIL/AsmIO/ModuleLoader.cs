namespace DistIL.AsmIO;

using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// TODO: Consider loading entities and metadata lazily - full module loading takes a *long* time
internal class ModuleLoader
{
    public readonly PEReader _pe;
    public readonly MetadataReader _reader;
    public readonly ModuleDef _mod;
    private readonly TableEntryCache _tables;

    public ModuleLoader(PEReader pe, ModuleDef mod)
    {
        _pe = pe;
        _reader = pe.GetMetadataReader();
        _mod = mod;
        _tables = new TableEntryCache(_reader);

        var asmDef = _reader.GetAssemblyDefinition();
        _mod.AsmName = asmDef.GetAssemblyName();
        
        var modDef = _reader.GetModuleDefinition();
        _mod.ModName = _reader.GetString(modDef.Name);
    }

    public void Load()
    {
        foreach (var handle in _reader.AssemblyReferences) {
            var info = _reader.GetAssemblyReference(handle);
            var asm = _mod.Resolver.Resolve(info.GetAssemblyName(), throwIfNotFound: true);
            _tables.Set(handle, asm);
        }
        foreach (var handle in _reader.TypeReferences) {
            var info = _reader.GetTypeReference(handle);
            var type = ResolveTypeRef(info);
            _tables.Set(handle, type);
        }

        foreach (var handle in _reader.TypeDefinitions) {
            var info = _reader.GetTypeDefinition(handle);
            var type = new TypeDef(_mod, GetOptString(info.Namespace), _reader.GetString(info.Name), info.Attributes);

            type._handle = handle;
            type._customAttribs = LoadCustomAttribs(info.GetCustomAttributes());

            if ((type.Attribs & (TypeAttributes.SequentialLayout | TypeAttributes.ExplicitLayout)) != 0) {
                var layout = info.GetLayout();
                type.LayoutPack = layout.PackingSize;
                type.LayoutSize = layout.Size;
            }
            if (type.IsNested) {
                // ECMA says that nested type rows should come after their parents,
                // thus doing this here should be fine for conforming files.
                type.SetDeclaringType(GetType(info.GetDeclaringType()));
            }

            _mod._typeDefs.Add(type);
            _tables.Set(handle, type);
        }
        foreach (var handle in _reader.ExportedTypes) {
            _mod._exportedTypes.Add(ResolveExportedType(handle));
        }

        // Other module stuff
        int entryPointToken = _pe.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress ?? 0;
        if (entryPointToken != 0) {
            _mod.EntryPoint = GetMethod(MetadataTokens.MethodDefinitionHandle(entryPointToken));
        }

        var asmCustomAttribs = LoadCustomAttribs(_reader.GetAssemblyDefinition().GetCustomAttributes());
        if (asmCustomAttribs != null) {
            _mod._asmCustomAttribs.AddRange(asmCustomAttribs);
        }
        var modCustomAttribs = LoadCustomAttribs(_reader.GetModuleDefinition().GetCustomAttributes());
        if (modCustomAttribs != null) {
            _mod._modCustomAttribs.AddRange(modCustomAttribs);
        }
    }

    private TypeDef ResolveTypeRef(TypeReference info)
    {
        string? ns = GetOptString(info.Namespace);
        string name = _reader.GetString(info.Name);
        var scope = GetEntity(info.ResolutionScope);
        TypeDef? type = null;

        if (scope is ModuleDef mod) {
            type = mod.FindType(ns, name);

            if (type != null && type.Module != mod) {
                _mod._typeRefRoots[type] = (ModuleDef)scope;
            }
        } else if (scope is TypeDef parent) {
            type = parent.FindNestedType(name);
        }
        return type ?? throw new InvalidOperationException($"Could not resolve referenced type '{ns}.{name}'");
    }
    private TypeDef ResolveExportedType(ExportedTypeHandle handle)
    {
        var info = _reader.GetExportedType(handle);
        string name = _reader.GetString(info.Name);
        string? ns = GetOptString(info.Namespace);
        TypeDef? impl;

        if (info.Implementation.Kind == HandleKind.ExportedType) {
            var parent = ResolveExportedType((ExportedTypeHandle)info.Implementation);
            impl = parent.FindNestedType(name);
        } else {
            var asm = (ModuleDef)GetEntity(info.Implementation);
            impl = asm.FindType(ns, name) 
                ?? throw new NotImplementedException(); // TODO: resolve recursive type exports
        }
        return impl ?? throw new InvalidOperationException($"Could not resolve forwarded type '{ns}.{name}'");
    }

    private MethodDesc ResolveMethod(MemberReference info)
    {
        var rootParent = (TypeDesc)GetEntity(info.Parent);
        string name = _reader.GetString(info.Name);
        var signature = new SignatureDecoder(this, info.Signature).DecodeMethodSig();

        for (var parent = rootParent; parent != null; parent = parent.BaseType) {
            var method = parent.FindMethod(name, signature, throwIfNotFound: false);
            if (method != null) {
                return method;
            }
            if (parent.BaseType is TypeSpec) {
                // TODO: ResolveMethod() for generic type inheritance
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

    public List<FieldDef> LoadFields(TypeDef parent)
    {
        var typeInfo = _reader.GetTypeDefinition(parent._handle);
        var list = new List<FieldDef>();

        foreach (var handle in typeInfo.GetFields()) {
            var info = _reader.GetFieldDefinition(handle);

            var sigDecoder = new SignatureDecoder(this, info.Signature, new GenericContext(parent));
            sigDecoder.ExpectHeader(SignatureKind.Field);
            var type = sigDecoder.DecodeTypeSig();

            var field = new FieldDef(parent, type, _reader.GetString(info.Name), info.Attributes);

            if (field.HasDefaultValue) {
                field.DefaultValue = GetConst(info.GetDefaultValue());
            }
            if (field.HasLayoutOffset) {
                field.LayoutOffset = info.GetOffset();
            }
            if (field.Attribs.HasFlag(FieldAttributes.HasFieldRVA)) {
                int rva = info.GetRelativeVirtualAddress();
                var data = _pe.GetSectionData(rva);
                int size = FieldDef.GetMappedDataSize(field.Type);
                unsafe { field.MappedData = new Span<byte>(data.Pointer, size).ToArray(); }
            }
            if (field.Attribs.HasFlag(FieldAttributes.HasFieldMarshal)) {
                field.MarshallingDesc = _reader.GetBlobBytes(info.GetMarshallingDescriptor());
            }
            field._customAttribs = LoadCustomAttribs(info.GetCustomAttributes());

            list.Add(field);
            _tables.Set(handle, field);
        }
        return list;
    }
    public List<MethodDef> LoadMethods(TypeDef parent)
    {
        var typeInfo = _reader.GetTypeDefinition(parent._handle);
        var list = new List<MethodDef>();

        foreach (var handle in typeInfo.GetMethods()) {
            var info = _reader.GetMethodDefinition(handle);

            // II.2.3.2.1 MethodDefSig
            var sigReader = _reader.GetBlobReader(info.Signature);
            var header = sigReader.ReadSignatureHeader();
            Ensure.That(header.Kind == SignatureKind.Method);
            Ensure.That(!header.HasExplicitThis); // not impl
            Ensure.That(header.IsInstance == !info.Attributes.HasFlag(MethodAttributes.Static));

            var genPars = Array.Empty<GenericParamType>();

            if (header.IsGeneric) {
                LoadGenericParams(handle, out genPars);
                Ensure.That(sigReader.ReadCompressedInteger() == genPars.Length);
            }
            int numParams = sigReader.ReadCompressedInteger();

            var sigDec = new SignatureDecoder(this, sigReader, new GenericContext(parent.GenericParams, genPars));
            var retSig = sigDec.DecodeTypeSig();

            var pars = ImmutableArray.CreateBuilder<ParamDef>(numParams + (header.IsInstance ? 1 : 0));

            if (header.IsInstance) {
                // It's illegal for local variables to be typed with a generic type def.
                // Use a unbound spec for `this` type instead.
                var instanceType = parent.GetSpec(GenericContext.Empty);
                pars.Add(new ParamDef(instanceType.IsValueType ? instanceType.CreateByref() : instanceType, "this"));
            }
            for (int i = 0; i < numParams; i++) {
                pars.Add(new ParamDef(sigDec.DecodeTypeSig(), "", 0));
            }

            var method = new MethodDef(
                parent, retSig, pars.MoveToImmutable(),
                _reader.GetString(info.Name), info.Attributes, info.ImplAttributes, genPars
            );

            LoadParams(method, info);
            method._bodyRva = info.RelativeVirtualAddress;
            method._customAttribs = LoadCustomAttribs(info.GetCustomAttributes());

            if ((info.Attributes & MethodAttributes.PinvokeImpl) != 0) {
                var imp = info.GetImport();
                var mod = _reader.GetModuleReference(imp.Module);

                method.ImportInfo = new ImportDesc(
                    _reader.GetString(mod.Name),
                    _reader.GetString(imp.Name),
                    imp.Attributes);
            }

            list.Add(method);
            _tables.Set(handle, method);
        }
        return list;
    }
    private void LoadParams(MethodDef method, MethodDefinition info)
    {
        foreach (var parHandle in info.GetParameters()) {
            var parInfo = _reader.GetParameter(parHandle);
            int index = parInfo.SequenceNumber;
            ParamDef par;

            if (index > 0 && index <= method.Params.Length) {
                par = method.Params[index - (method.IsStatic ? 1 : 0)]; // we always have a `this` param
                par.Name = _reader.GetString(parInfo.Name);
            } else {
                par = method.ReturnParam;
            }
            par.Attribs = parInfo.Attributes;

            if (par.Attribs.HasFlag(ParameterAttributes.HasDefault)) {
                par.DefaultValue = GetConst(parInfo.GetDefaultValue());
            }
            if (par.Attribs.HasFlag(ParameterAttributes.HasFieldMarshal)) {
                par.MarshallingDesc = _reader.GetBlobBytes(parInfo.GetMarshallingDescriptor());
            }
            par._customAttribs = LoadCustomAttribs(parInfo.GetCustomAttributes());
        }
    }

    public List<PropertyDef> LoadProperties(TypeDef parent)
    {
        var typeInfo = _reader.GetTypeDefinition(parent._handle);
        var list = new List<PropertyDef>();

        foreach (var handle in typeInfo.GetProperties()) {
            var info = _reader.GetPropertyDefinition(handle);

            var sig = new SignatureDecoder(this, info.Signature, new GenericContext(parent)).DecodeMethodSig();
            var accs = info.GetAccessors();

            var prop = new PropertyDef(
                parent, _reader.GetString(info.Name), sig,
                accs.Getter.IsNil ? null : GetMethod(accs.Getter),
                accs.Setter.IsNil ? null : GetMethod(accs.Setter),
                accs.Others.Select(GetMethod).ToArray(),
                GetConst(info.GetDefaultValue()),
                info.Attributes
            );
            prop._customAttribs = LoadCustomAttribs(info.GetCustomAttributes());

            list.Add(prop);
        }
        return list;
    }
    public List<EventDef> LoadEvents(TypeDef parent)
    {
        var typeInfo = _reader.GetTypeDefinition(parent._handle);
        var list = new List<EventDef>();

        foreach (var handle in typeInfo.GetEvents()) {
            var info = _reader.GetEventDefinition(handle);

            var type = (TypeDesc)GetEntity(info.Type);
            var accs = info.GetAccessors();

            var evt = new EventDef(
                parent, _reader.GetString(info.Name), type,
                accs.Adder.IsNil ? null : GetMethod(accs.Adder),
                accs.Remover.IsNil ? null : GetMethod(accs.Remover),
                accs.Raiser.IsNil ? null : GetMethod(accs.Raiser),
                accs.Others.Select(GetMethod).ToArray(),
                info.Attributes
            );
            evt._customAttribs = LoadCustomAttribs(info.GetCustomAttributes());

            list.Add(evt);
        }
        return list;
    }

    public TypeDefOrSpec? GetBaseType(TypeDefinitionHandle parentHandle)
    {
        var baseTypeHandle = _reader.GetTypeDefinition(parentHandle).BaseType;
        return baseTypeHandle.IsNil ? null : (TypeDefOrSpec)GetEntity(baseTypeHandle);
    }
    public List<TypeDesc> LoadInterfaces(TypeDef parent)
    {
        var typeInfo = _reader.GetTypeDefinition(parent._handle);
        var list = new List<TypeDesc>();

        foreach (var itfHandle in typeInfo.GetInterfaceImplementations()) {
            var itf = _reader.GetInterfaceImplementation(itfHandle);
            var type = (TypeDesc)GetEntity(itf.Interface);

            list.Add(type);
            parent.SetImplCustomAttribs((type, null), LoadCustomAttribs(itf.GetCustomAttributes()));
        }
        return list;
    }
    public Dictionary<MethodDesc, MethodDef> LoadMethodImpls(TypeDef parent)
    {
        var typeInfo = _reader.GetTypeDefinition(parent._handle);
        var map = new Dictionary<MethodDesc, MethodDef>();

        foreach (var implHandle in typeInfo.GetMethodImplementations()) {
            var impl = _reader.GetMethodImplementation(implHandle);
            var body = (MethodDef)GetEntity(impl.MethodBody);
            var decl = (MethodDesc)GetEntity(impl.MethodDeclaration);

            map.Add(decl, body);
            parent.SetImplCustomAttribs((decl, body), LoadCustomAttribs(impl.GetCustomAttributes()));
        }
        return map;
    }

    public void LoadGenericParams(EntityHandle parentHandle, out GenericParamType[] genPars)
    {
        GenericParameterHandleCollection collection;

        if (parentHandle.Kind == HandleKind.TypeDefinition) {
            var info = _reader.GetTypeDefinition((TypeDefinitionHandle)parentHandle);
            collection = info.GetGenericParameters();
        } else if (parentHandle.Kind == HandleKind.MethodDefinition) {
            var info = _reader.GetMethodDefinition((MethodDefinitionHandle)parentHandle);
            collection = info.GetGenericParameters();
        } else {
            throw new UnreachableException();
        }

        var list = new List<GenericParamType>();
        bool isMethod = parentHandle.Kind == HandleKind.MethodDefinition;

        foreach (var handle in collection) {
            var info = _reader.GetGenericParameter(handle);

            var par = new GenericParamType(info.Index, isMethod, _reader.GetString(info.Name), info.Attributes);
            par.CustomAttribs = LoadCustomAttribs(info.GetCustomAttributes());

            Debug.Assert(par.Index == list.Count);
            list.Add(par);
        }

        // Output field *must* be assigned before we start reading
        // constraint types in order to prevent infinite recursion.
        genPars = list.ToArray();

        foreach (var handle in collection) {
            var info = _reader.GetGenericParameter(handle);
            genPars[info.Index].Constraints = LoadGenericParamConstraints(info.GetConstraints());
        }
    }
    private ImmutableArray<GenericParamConstraint> LoadGenericParamConstraints(GenericParameterConstraintHandleCollection list)
    {
        var builder = ImmutableArray.CreateBuilder<GenericParamConstraint>(list.Count);

        foreach (var handle in list) {
            var info = _reader.GetGenericParameterConstraint(handle);
            
            builder.Add(new GenericParamConstraint(
                GetTypeSig(info.Type),
                LoadCustomAttribs(info.GetCustomAttributes())
            ));
        }
        return builder.DrainToImmutable();
    }

    public CustomAttrib[] LoadCustomAttribs(CustomAttributeHandleCollection handles)
    {
        if (handles.Count == 0) {
            return [];
        }
        var attribs = new CustomAttrib[handles.Count];
        int index = 0;

        foreach (var handle in handles) {
            attribs[index++] = new CustomAttrib(_mod, handle);
        }
        return attribs;
    }

    public FuncPtrType DecodeMethodSig(StandaloneSignatureHandle handle)
    {
        var info = _reader.GetStandaloneSignature(handle);
        Ensure.That(info.GetCustomAttributes().Count == 0);
        
        return new FuncPtrType(new SignatureDecoder(this, info.Signature).DecodeMethodSig());
    }

    public string? GetOptString(StringHandle handle)
        => handle.IsNil ? null : _reader.GetString(handle);

    public object? GetConst(ConstantHandle handle)
    {
        if (handle.IsNil) {
            return null;
        }
        var cst = _reader.GetConstant(handle);
        var blob = _reader.GetBlobReader(cst.Value);

        return blob.ReadConstant(cst.TypeCode);
    }

    public EntityDesc GetEntity(EntityHandle handle)
    {
        var entity = _tables.Get(handle);

        if (entity == null) {
            entity = LoadEntity(handle);
            _tables.Set(handle, entity);
        }
        return entity ?? throw new InvalidOperationException("Table entry is not yet populated");
    }
    public TypeDef GetType(TypeDefinitionHandle handle) => (TypeDef)GetEntity(handle);
    public MethodDef GetMethod(MethodDefinitionHandle handle) => (MethodDef)GetEntity(handle);
    public TypeSig GetTypeSig(EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeSpecification) {
            var info = _reader.GetTypeSpecification((TypeSpecificationHandle)handle);
            return new SignatureDecoder(this, info.Signature).DecodeTypeSig();
        }
        return (TypeDesc)GetEntity(handle);
    }

    private EntityDesc LoadEntity(EntityHandle handle)
    {
        switch (handle.Kind) {
            // For any given parent type, all belonging methods and fields will be initialized
            // once the respective properties in TypeDef are accessed.
            case HandleKind.MethodDefinition: {
                var info = _reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                var type = GetType(info.GetDeclaringType());
                _ = type.Methods; // init lazy property
                return _tables.Get(handle)!;
            }
            case HandleKind.FieldDefinition: {
                var info = _reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                var type = GetType(info.GetDeclaringType());
                _ = type.Fields; // init lazy property
                return _tables.Get(handle)!;
            }
            case HandleKind.TypeSpecification: {
                var info = _reader.GetTypeSpecification((TypeSpecificationHandle)handle);
                var sig = new SignatureDecoder(this, info.Signature).DecodeTypeSig();
                Ensure.That(!sig.HasModifiers); // should use GetTypeSig() instead of GetEntity()
                return sig.Type;
            }
            case HandleKind.MethodSpecification: {
                var info = _reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                var method = (MethodDefOrSpec)GetEntity(info.Method);
                var decoder = new SignatureDecoder(this, info.Signature, new GenericContext(method));

                Ensure.That(decoder.Reader.ReadSignatureHeader().Kind == SignatureKind.MethodSpecification);
                return new MethodSpec(method.DeclaringType, method.Definition, decoder.DecodeGenArgs());
            }
            case HandleKind.MemberReference: {
                var info = _reader.GetMemberReference((MemberReferenceHandle)handle);

                return info.GetKind() switch {
                    MemberReferenceKind.Field => ResolveField(info),
                    MemberReferenceKind.Method => ResolveMethod(info)
                };
            }
            default: throw new NotSupportedException();
        }
    }

    class TableEntryCache
    {
        static readonly TableIndex[] s_Tables = {
            TableIndex.AssemblyRef,
            TableIndex.TypeRef, TableIndex.MemberRef,
            TableIndex.TypeDef, TableIndex.TypeSpec,
            TableIndex.Field,
            TableIndex.MethodDef, TableIndex.MethodSpec
        };
        readonly EntityDesc[] _entities; // entities laid out linearly
        readonly AbsRange[] _ranges; // inclusive range in _entities for each TableIndex

        public TableEntryCache(MetadataReader reader)
        {
            _ranges = new AbsRange[s_Tables.Max(v => (int)v) + 1];

            int startIdx = 0;
            foreach (var tableIdx in s_Tables) {
                int numRows = reader.GetTableRowCount(tableIdx);
                _ranges[(int)tableIdx] = AbsRange.FromSlice(startIdx, numRows);
                startIdx += numRows;
            }
            _entities = new EntityDesc[startIdx + 1];
        }
        public EntityDesc? Get(EntityHandle handle)
        {
            // FIXME: Create() will update the ranges before populating the table,
            // so that ResolveTypeRef() can call Get() with a nested type.
            // There must be a better way to do that...
            int index = GetIndex(handle);
            return _entities[index];
        }

        public void Set(EntityHandle handle, EntityDesc entity)
        {
            int idx = GetIndex(handle);
            Debug.Assert(_entities[idx] == null || _entities[idx] == entity, "Trying to replace already existing entity");
            _entities[idx] = entity;
        }

        private int GetIndex(EntityHandle handle)
        {
            var tableIdx = (int)handle.Kind;
            Debug.Assert(Array.IndexOf(s_Tables, (TableIndex)tableIdx) >= 0);

            var (start, end) = _ranges[tableIdx];
            int index = start + MetadataTokens.GetRowNumber(handle) - 1; // rows are 1 based
            Ensure.That(index < end);
            return index;
        }
    }
}