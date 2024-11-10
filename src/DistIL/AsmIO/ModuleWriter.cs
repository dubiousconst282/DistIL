namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal partial class ModuleWriter
{
    readonly ModuleDef _mod;
    readonly MetadataBuilder _builder;
    readonly MethodBodyStreamEncoder _bodyEncoder;
    private BlobBuilder? _fieldDataStream;

    readonly Dictionary<EntityDesc, EntityHandle> _handleMap = new();
    readonly Dictionary<string, ModuleReferenceHandle> _moduleRefs = new();
    // Generic parameters must be sorted based on the coded parent entity handle, we do that in a later pass.
    readonly List<EntityDesc> _genericDefs = new();

    public ModuleWriter(ModuleDef mod)
    {
        _mod = mod;
        _builder = new MetadataBuilder();
        _bodyEncoder = new MethodBodyStreamEncoder(new BlobBuilder());

    }

    public void BuildTables()
    {
        // https:// github.com/dotnet/runtime/blob/main/src/libraries/System.Reflection.Metadata/tests/PortableExecutable/PEBuilderTests.cs
        var mainModHandle = _builder.AddModule(
            0, 
            AddString(_mod.ModName), 
            _builder.GetOrAddGuid(Guid.NewGuid()),
            _builder.GetOrAddGuid(default),
            _builder.GetOrAddGuid(default)
        );

        var asmName = _mod.AsmName;
        var mainAsmHandle = _builder.AddAssembly(
            AddString(asmName.Name!),
            asmName.Version!,
            AddString(asmName.CultureName),
            AddBlob(asmName.GetPublicKey()),
            (AssemblyFlags)asmName.Flags,
#pragma warning disable SYSLIB0037 // AssemblyName.HashAlgorithm is obsolete
            (AssemblyHashAlgorithm)asmName.HashAlgorithm
#pragma warning restore SYSLIB0037
        );
        _handleMap.Add(_mod, mainAsmHandle);

        AllocHandles();
        EmitEntities();

        EmitCustomAttribs(mainAsmHandle, _mod.GetCustomAttribs(forAssembly: true));
        EmitCustomAttribs(mainModHandle, _mod.GetCustomAttribs(forAssembly: false));
    }

    private void EmitEntities()
    {
        foreach (var type in _mod.TypeDefs) {
            EmitType(type);
        }
        EmitPendingGenericParams();
    }

    private void EmitType(TypeDef type)
    {
        var firstFieldHandle = MetadataTokens.FieldDefinitionHandle(_builder.GetRowCount(TableIndex.Field) + 1);
        var firstMethodHandle = MetadataTokens.MethodDefinitionHandle(_builder.GetRowCount(TableIndex.MethodDef) + 1);

        var handle = _builder.AddTypeDefinition(
            type.Attribs,
            AddString(type.Namespace),
            AddString(type.Name),
            type.BaseType == null ? default : GetHandle(type.BaseType),
            firstFieldHandle,
            firstMethodHandle
        );
        Debug.Assert(_handleMap[type] == handle);
        Debug.Assert(type.Fields.Count == 0 || _handleMap[type.Fields[0]] == firstFieldHandle);
        Debug.Assert(type.Methods.Count == 0 || _handleMap[type.Methods[0]] == firstMethodHandle);

        if (type.IsGeneric) {
            _genericDefs.Add(type);
        }
        if (type.IsNested) {
            _builder.AddNestedType(handle, (TypeDefinitionHandle)GetHandle(type.DeclaringType));
        }
        foreach (var itf in type.Interfaces) {
            var itfHandle = _builder.AddInterfaceImplementation(handle, GetHandle(itf));
            EmitCustomAttribs(itfHandle, type.GetCustomAttribs(itf));
        }
        foreach (var (decl, impl) in type.MethodImpls) {
            Debug.Assert(decl is not MethodSpec { IsBoundGeneric: true });

            var implHandle = _builder.AddMethodImplementation(handle, GetHandle(impl), GetHandle(decl));
            EmitCustomAttribs(implHandle, type.GetCustomAttribs(decl, impl));
        }
        
        foreach (var field in type.Fields) {
            EmitField(field);
        }
        foreach (var method in type.Methods) {
            EmitMethod(method);
        }
        int propIdx = 0;
        foreach (var prop in type.Properties) {
            var propHandle = EmitProp(prop);

            if (propIdx++ == 0) {
                _builder.AddPropertyMap(handle, propHandle);
            }
        }

        int evtIdx = 0;
        foreach (var evt in type.Events) {
            var evtHandle = EmitEvent(evt);

            if (evtIdx++ == 0) {
                _builder.AddEventMap(handle, evtHandle);
            }
        }
        if (type.HasCustomLayout) {
            _builder.AddTypeLayout(handle, (ushort)type.LayoutPack, (uint)type.LayoutSize);
        }
        EmitCustomAttribs(handle, type.GetCustomAttribs());
    }

    private void EmitField(FieldDef field)
    {
        var handle = _builder.AddFieldDefinition(
            field.Attribs,
            AddString(field.Name),
            EncodeFieldSig(field)
        );
        Debug.Assert(_handleMap[field] == handle);

        if (field.Attribs.HasFlag(FieldAttributes.HasFieldRVA)) {
            _fieldDataStream ??= new();
            _builder.AddFieldRelativeVirtualAddress(handle, _fieldDataStream.Count);
            _fieldDataStream.WriteBytes(field.MappedData!);
            _fieldDataStream.Align(ManagedPEBuilder.MappedFieldDataAlignment);
        }
        if (field.Attribs.HasFlag(FieldAttributes.HasFieldMarshal)) {
            _builder.AddMarshallingDescriptor(handle, _builder.GetOrAddBlob(field.MarshallingDesc!));
        }
        if (field.HasDefaultValue) {
            _builder.AddConstant(handle, field.DefaultValue);
        }
        if (field.HasLayoutOffset) {
            _builder.AddFieldLayout(handle, field.LayoutOffset);
        }
        EmitCustomAttribs(handle, field.GetCustomAttribs());
    }

    private void EmitMethod(MethodDef method)
    {
        var signature = EncodeMethodSig(method);
        int bodyOffset = EmitMethodBodyRVA(method.ILBody);
        var firstParamHandle = MetadataTokens.ParameterHandle(_builder.GetRowCount(TableIndex.Param) + 1);

        var handle = _builder.AddMethodDefinition(
            method.Attribs, method.ImplAttribs,
            AddString(method.Name),
            signature, bodyOffset, firstParamHandle
        );
        Debug.Assert(_handleMap[method] == handle);

        EmitParam(method.ReturnParam, 0);

        var pars = method.StaticParams;
        for (int i = 0; i < pars.Length; i++) {
            EmitParam(pars[i], i + 1);
        }
        if (method.IsGeneric) {
            _genericDefs.Add(method);
        }
        if (method.ImportInfo != null) {
            var imp = method.ImportInfo;
            _builder.AddMethodImport(handle, imp.Attribs, AddString(imp.FunctionName), GetOrAddModRef(imp.ModuleName));
        }
        EmitCustomAttribs(handle, method.GetCustomAttribs());
    }
    private ModuleReferenceHandle GetOrAddModRef(string moduleName)
    {
        if (!_moduleRefs.TryGetValue(moduleName, out var handle)) {
            handle = _builder.AddModuleReference(_builder.GetOrAddString(moduleName));
            _moduleRefs.Add(moduleName, handle);
        }
        return handle;
    }

    private void EmitParam(ParamDef par, int index)
    {
        var handle = _builder.AddParameter(par.Attribs, AddString(par.Name), index);

        if (par.Attribs.HasFlag(ParameterAttributes.HasDefault)) {
            _builder.AddConstant(handle, par.DefaultValue);
        }
        if (par.Attribs.HasFlag(ParameterAttributes.HasFieldMarshal)) {
            _builder.AddMarshallingDescriptor(handle, _builder.GetOrAddBlob(par.MarshallingDesc!));
        }
        EmitCustomAttribs(handle, par.GetCustomAttribs());
    }

    private PropertyDefinitionHandle EmitProp(PropertyDef prop)
    {
        var sigBlob = EncodeSig(b => EncodeMethodSig(b, prop.Sig, isPropSig: true));
        var handle = _builder.AddProperty(prop.Attribs, AddString(prop.Name), sigBlob);

        Link(handle, prop.Getter, MethodSemanticsAttributes.Getter);
        Link(handle, prop.Setter, MethodSemanticsAttributes.Setter);

        foreach (var otherAcc in prop.OtherAccessors) {
            Link(handle, otherAcc, MethodSemanticsAttributes.Other);
        }
        EmitCustomAttribs(handle, prop.GetCustomAttribs());

        return handle;
    }
    private EventDefinitionHandle EmitEvent(EventDef evt)
    {
        var handle = _builder.AddEvent(evt.Attribs, AddString(evt.Name), GetHandle(evt.Type));

        Link(handle, evt.Adder, MethodSemanticsAttributes.Adder);
        Link(handle, evt.Remover, MethodSemanticsAttributes.Remover);
        Link(handle, evt.Raiser, MethodSemanticsAttributes.Raiser);

        foreach (var otherAcc in evt.OtherAccessors) {
            Link(handle, otherAcc, MethodSemanticsAttributes.Other);
        }
        EmitCustomAttribs(handle, evt.GetCustomAttribs());
        return handle;
    }
    private void Link(EntityHandle assoc, MethodDef? method, MethodSemanticsAttributes kind)
    {
        if (method != null) {
            _builder.AddMethodSemantics(assoc, kind, (MethodDefinitionHandle)GetHandle(method));
        }
    }

    private void EmitPendingGenericParams()
    {
        _genericDefs.Sort((a, b) => {
            int ai = CodedIndex.TypeOrMethodDef(_handleMap[a]);
            int bi = CodedIndex.TypeOrMethodDef(_handleMap[b]);
            return ai - bi;
        });

        foreach (var entity in _genericDefs) {
            var handle = _handleMap[entity];
            var genPars = (entity as TypeDef)?.GenericParams ?? ((MethodDef)entity).GenericParams;

            foreach (var par in genPars) {
                var parHandle = _builder.AddGenericParameter(handle, par.Attribs, AddString(par.Name), par.Index);
                EmitCustomAttribs(parHandle, par.CustomAttribs);

                foreach (var constraint in par.Constraints) {
                    var constrHandle = _builder.AddGenericParameterConstraint(parHandle, GetSigHandle(constraint.Sig));
                    EmitCustomAttribs(constrHandle, constraint.CustomAttribs);
                }
            }
        }
    }

    private void EmitCustomAttribs(EntityHandle parentHandle, IList<CustomAttrib>? attribs)
    {
        if (attribs == null) return;

        foreach (var attrib in attribs) {
            _builder.AddCustomAttribute(
                parentHandle,
                GetHandle(attrib.Constructor),
                _builder.GetOrAddBlob(attrib.GetEncodedBlob())
            );
        }
    }

    public void Serialize(Stream peStream, Stream? pdbStream, string pePath)
    {
        var imageChars = 
            Characteristics.LargeAddressAware |
            Characteristics.ExecutableImage |
            (_mod.EntryPoint == null ? Characteristics.Dll : 0);

        var header = new PEHeaderBuilder(imageCharacteristics: imageChars);
        var entryPoint = _mod.EntryPoint == null ? default : (MethodDefinitionHandle)_handleMap[_mod.EntryPoint];

        var debugDirBuilder = new DebugDirectoryBuilder();
        var debugSymbols = _mod.GetDebugSymbols(create: false);

        if (pdbStream != null && debugSymbols != null) {
            var pdbEmitter = new PdbBuilder(this);
            debugSymbols.Write(pdbEmitter);

            // Serialize
            var pdbBuilder = new PortablePdbBuilder(pdbEmitter.TableBuilder, _builder.GetRowCounts(), entryPoint);
            var pdbBlob = new BlobBuilder();
            var pdbContentId = pdbBuilder.Serialize(pdbBlob);
            pdbBlob.WriteContentTo(pdbStream);

            debugDirBuilder.AddCodeViewEntry(Path.ChangeExtension(pePath, ".pdb"), pdbContentId, pdbBuilder.FormatVersion);
        }

        var peBuilder = new ManagedPEBuilder(
            header: header,
            metadataRootBuilder: new MetadataRootBuilder(_builder),
            ilStream: _bodyEncoder.Builder,
            mappedFieldData: _fieldDataStream,
            entryPoint: entryPoint,
            debugDirectoryBuilder: debugDirBuilder
        );
        var blob = new BlobBuilder();
        peBuilder.Serialize(blob);
        blob.WriteContentTo(peStream);
    }
}