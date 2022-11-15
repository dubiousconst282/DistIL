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

    readonly Dictionary<Entity, EntityHandle> _handleMap = new();
    //Generic parameters must be sorted based on the coded parent entity handle, we do that in a later pass.
    readonly List<EntityDesc> _genericDefs = new();

    public ModuleWriter(ModuleDef mod)
    {
        _mod = mod;
        _builder = new MetadataBuilder();
        _bodyEncoder = new MethodBodyStreamEncoder(new BlobBuilder());

    }

    public void Emit(BlobBuilder peBlob)
    {
        //https://github.com/dotnet/runtime/blob/main/src/libraries/System.Reflection.Metadata/tests/PortableExecutable/PEBuilderTests.cs
        var mainModHandle = _builder.AddModule(
            0, 
            AddString(_mod.ModName), 
            _builder.GetOrAddGuid(default),
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

        EmitCustomAttribs(_mod, mainAsmHandle);
        EmitCustomAttribs(_mod, mainModHandle, CustomAttribLink.Type.Module);

        SerializePE(peBlob);
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

        if (type.GenericParams.Length > 0) {
            _genericDefs.Add(type);
        }
        if (type.IsNested) {
            _builder.AddNestedType(handle, (TypeDefinitionHandle)GetHandle(type.DeclaringType));
        }
        int itfIndex = 0;
        foreach (var itf in type.Interfaces) {
            var itfHandle = _builder.AddInterfaceImplementation(handle, GetHandle(itf));
            EmitCustomAttribs(type, itfHandle, CustomAttribLink.Type.InterfaceImpl, itfIndex++);
        }
        foreach (var (decl, impl) in type.InterfaceMethodImpls) {
            var implHandle = _builder.AddMethodImplementation(handle, GetHandle(impl), GetHandle(decl));
            EmitCustomAttribs(impl, implHandle, CustomAttribLink.Type.InterfaceImpl);
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
        EmitCustomAttribs(type, handle);

        PropertyDefinitionHandle EmitProp(PropertyDef prop)
        {
            var handle = _builder.AddProperty(prop.Attribs, AddString(prop.Name), EncodeMethodSig(prop.Sig));

            Link(handle, prop.Getter, MethodSemanticsAttributes.Getter);
            Link(handle, prop.Setter, MethodSemanticsAttributes.Setter);

            foreach (var otherAcc in prop.OtherAccessors) {
                Link(handle, otherAcc, MethodSemanticsAttributes.Other);
            }
            EmitCustomAttribs(prop, handle);

            return handle;
        }
        EventDefinitionHandle EmitEvent(EventDef evt)
        {
            var handle = _builder.AddEvent(evt.Attribs, AddString(evt.Name), GetHandle(evt.Type));

            Link(handle, evt.Adder, MethodSemanticsAttributes.Adder);
            Link(handle, evt.Remover, MethodSemanticsAttributes.Remover);
            Link(handle, evt.Raiser, MethodSemanticsAttributes.Raiser);

            foreach (var otherAcc in evt.OtherAccessors) {
                Link(handle, otherAcc, MethodSemanticsAttributes.Other);
            }
            EmitCustomAttribs(evt, handle);
            return handle;
        }
        void Link(EntityHandle assoc, MethodDef? method, MethodSemanticsAttributes kind)
        {
            if (method != null) {
                _builder.AddMethodSemantics(assoc, kind, (MethodDefinitionHandle)GetHandle(method));
            }
        }
    }

    private void EmitField(FieldDef field)
    {
        var handle = _builder.AddFieldDefinition(
            field.Attribs,
            AddString(field.Name),
            EncodeFieldSig(field)
        );
        Debug.Assert(_handleMap[field] == handle);

        if (field.MappedData != null) {
            _fieldDataStream ??= new();
            _builder.AddFieldRelativeVirtualAddress(handle, _fieldDataStream.Count);
            _fieldDataStream.WriteBytes(field.MappedData);
            _fieldDataStream.Align(ManagedPEBuilder.MappedFieldDataAlignment);
        }
        if (field.Attribs.HasFlag(FieldAttributes.HasDefault)) {
            _builder.AddConstant(handle, field.DefaultValue);
        }
        if (field.LayoutOffset >= 0) {
            _builder.AddFieldLayout(handle, field.LayoutOffset);
        }
        if (field.MarshallingDesc != null) {
            _builder.AddMarshallingDescriptor(handle, _builder.GetOrAddBlob(field.MarshallingDesc));
        }
        EmitCustomAttribs(field, handle);
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

        var returnCAs = _mod.GetLinkedCustomAttribs(new(method, -1, CustomAttribLink.Type.MethodParam));
        if (returnCAs.Length > 0) {
            var parHandle = _builder.AddParameter(method.ReturnParam.Attribs, default, 0);
            EmitCustomAttribs(parHandle, returnCAs);
        }
        var pars = method.StaticParams;
        for (int i = 0; i < pars.Length; i++) {
            var par = pars[i];
            var parHandle = _builder.AddParameter(par.Attribs, AddString(par.Name), i + 1);

            if (par.Attribs.HasFlag(ParameterAttributes.HasDefault)) {
                _builder.AddConstant(parHandle, par.DefaultValue);
            }
            if (par.Attribs.HasFlag(ParameterAttributes.HasFieldMarshal)) {
                _builder.AddMarshallingDescriptor(parHandle, _builder.GetOrAddBlob(par.MarshallingDesc!));
            }
            EmitCustomAttribs(method, parHandle, CustomAttribLink.Type.MethodParam, i + (method.IsStatic ? 0 : 1));
        }
        if (method.GenericParams.Length > 0) {
            _genericDefs.Add(method);
        }
        EmitCustomAttribs(method, handle);
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

            foreach (var par in genPars.Cast<GenericParamType>()) {
                var parHandle = _builder.AddGenericParameter(handle, par.Attribs, AddString(par.Name), par.Index);
                EmitCustomAttribs(parHandle, par.GetCustomAttribs());

                for (int i = 0; i < par.Constraints.Length; i++) {
                    var constrHandle = _builder.AddGenericParameterConstraint(parHandle, GetSigHandle(par.Constraints[i]));

                    var constrAttribs = par._constraintCustomAttribs?.ElementAtOrDefault(i);
                    if (constrAttribs != null) {
                        EmitCustomAttribs(constrHandle, constrAttribs);
                    }
                }
            }
        }
    }

    private void EmitCustomAttribs(ModuleEntity entity, EntityHandle parentHandle, CustomAttribLink.Type linkType = default, int linkIndex = 0)
    {
        var attribs = _mod.GetLinkedCustomAttribs(new(entity, linkIndex, linkType));
        EmitCustomAttribs(parentHandle, attribs);
    }

    private void EmitCustomAttribs(EntityHandle parentHandle, IReadOnlyCollection<CustomAttrib> attribs)
    {
        foreach (var attrib in attribs) {
            _builder.AddCustomAttribute(
                parentHandle,
                GetHandle(attrib.Constructor),
                _builder.GetOrAddBlob(attrib.GetEncodedBlob())
            );
        }
    }

    private void SerializePE(BlobBuilder peBlob)
    {
        var imageChars = 
            Characteristics.LargeAddressAware |
            Characteristics.ExecutableImage |
            (_mod.EntryPoint == null ? Characteristics.Dll : 0);

        var header = new PEHeaderBuilder(imageCharacteristics: imageChars);

        var peBuilder = new ManagedPEBuilder(
            header: header,
            metadataRootBuilder: new MetadataRootBuilder(_builder),
            ilStream: _bodyEncoder.Builder,
            mappedFieldData: _fieldDataStream,
            entryPoint: _mod.EntryPoint == null ? default : (MethodDefinitionHandle)_handleMap[_mod.EntryPoint]
        );
        peBuilder.Serialize(peBlob);
    }
}