namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal class ModuleWriter
{
    readonly ModuleDef _mod;
    readonly MetadataBuilder _builder;
    readonly MethodBodyStreamEncoder _bodyEncoder;
    private BlobBuilder? _fieldDataStream;

    private Dictionary<Entity, EntityHandle> _handleMap = new();
    //We need to write generic parameters in a later pass, since they must be sorted
    private List<EntityDesc> _genericDefs = new();

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
            AddString(_mod.Name), 
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

        SerializePE(peBlob);
    }

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
    }

    private void EmitEntities()
    {
        foreach (var type in _mod.TypeDefs) {
            EmitType(type);
        }

        _genericDefs.Sort((a, b) => {
            int ai = CodedIndex.TypeOrMethodDef(_handleMap[a]);
            int bi = CodedIndex.TypeOrMethodDef(_handleMap[b]);
            return ai - bi;
        });
        foreach (var entity in _genericDefs) {
            var handle = _handleMap[entity];
            var genPars = (entity as TypeDef)?.GenericParams ?? ((MethodDef)entity).GenericParams;
            foreach (var par in genPars.Cast<GenericParamType>()) {
                _builder.AddGenericParameter(handle, par.Attribs, AddString(par.Name), par.Index);
            }
        }
    }

    private EntityHandle GetHandle(Entity entity)
    {
        if (!_handleMap.TryGetValue(entity, out var handle)) {
            _handleMap[entity] = handle = CreateRef(entity);
        }
        return handle;

        EntityHandle CreateRef(Entity entity)
        {
            switch (entity) {
                case TypeDef type: {
                    return _builder.AddTypeReference(
                        GetHandle((Entity?)type.DeclaringType ?? _mod._typeRefRoots.GetValueOrDefault(type, type.Module)),
                        AddString(type.Namespace),
                        AddString(type.Name)
                    );
                }
                case TypeDesc type: {
                    return _builder.AddTypeSpecification(
                        EncodeSig(b => EncodeType(b.TypeSpecificationSignature(), type))
                    );
                }
                case MethodSpec { GenericParams.Length: > 0 } method: {
                    return _builder.AddMethodSpecification(
                        GetHandle(method.Definition),
                        EncodeMethodSig(method)
                    );
                }
                case MethodDefOrSpec method: {
                    return _builder.AddMemberReference(
                        GetHandle(method.DeclaringType),
                        AddString(method.Name),
                        EncodeMethodSig(method.Definition)
                    );
                }
                case FieldDefOrSpec field: {
                    return _builder.AddMemberReference(
                        GetHandle(field.DeclaringType),
                        AddString(field.Name),
                        EncodeFieldSig(field)
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
    }
    private EntityHandle GetTypeHandle(TypeDesc type)
    {
        if (type is PrimType primType) {
            type = primType.GetDefinition(_mod);
        }
        return GetHandle(type);
    }
    private StandaloneSignatureHandle GetStandaloneSig(FuncPtrType type)
    {
        throw new NotImplementedException();
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
        Assert(_handleMap[type] == handle);
        Assert(type.Fields.Count == 0 || _handleMap[type.Fields[0]] == firstFieldHandle);
        Assert(type.Methods.Count == 0 || _handleMap[type.Methods[0]] == firstMethodHandle);

        if (type.GenericParams.Length > 0) {
            _genericDefs.Add(type);
        }
        if (type.IsNested) {
            _builder.AddNestedType(handle, (TypeDefinitionHandle)GetHandle(type.DeclaringType));
        }
        foreach (var field in type.Fields) {
            EmitField(field);
        }
        foreach (var method in type.Methods) {
            EmitMethod(method);
        }
        int propIdx = 0;
        foreach (var prop in type.Properties) {
            var propHandle = _builder.AddProperty(prop.Attribs, AddString(prop.Name), EncodePropertySig(prop));

            Link(propHandle, prop.Getter, MethodSemanticsAttributes.Getter);
            Link(propHandle, prop.Setter, MethodSemanticsAttributes.Setter);

            foreach (var otherAcc in prop.OtherAccessors) {
                Link(propHandle, otherAcc, MethodSemanticsAttributes.Other);
            }
            if (propIdx++ == 0) {
                _builder.AddPropertyMap(handle, propHandle);
            }
        }
        int evtIdx = 0;
        foreach (var evt in type.Events) {
            var evtHandle = _builder.AddEvent(evt.Attribs, AddString(evt.Name), GetHandle(evt.Type));

            Link(evtHandle, evt.Adder, MethodSemanticsAttributes.Adder);
            Link(evtHandle, evt.Remover, MethodSemanticsAttributes.Remover);
            Link(evtHandle, evt.Raiser, MethodSemanticsAttributes.Raiser);

            foreach (var otherAcc in evt.OtherAccessors) {
                Link(evtHandle, otherAcc, MethodSemanticsAttributes.Other);
            }
            if (evtIdx++ == 0) {
                _builder.AddEventMap(handle, evtHandle);
            }
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
        Assert(_handleMap[field] == handle);

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
        //TODO: _builder.AddMarshallingDescriptor()
    }

    private void EmitMethod(MethodDef method)
    {
        var signature = EncodeMethodSig(method);
        int bodyOffset = EmitBody(method.ILBody);
        var firstParamHandle = MetadataTokens.ParameterHandle(_builder.GetRowCount(TableIndex.Param) + 1);

        var pars = method.StaticParams;
        for (int i = 0; i < pars.Length; i++) {
            var par = pars[i];
            _builder.AddParameter(par.Attribs, AddString(par.Name), i + 1);
        }

        var handle = _builder.AddMethodDefinition(
            method.Attribs,
            method.ImplAttribs,
            AddString(method.Name),
            signature, bodyOffset, firstParamHandle
        );
        Assert(_handleMap[method] == handle);

        if (method.GenericParams.Length > 0) {
            _genericDefs.Add(method);
        }
    }

    private int EmitBody(ILMethodBody? body)
    {
        if (body == null) {
            return -1;
        }
        var localVarSigs = default(StandaloneSignatureHandle);
        var attribs = body.InitLocals ? MethodBodyAttributes.InitLocals : 0;

        if (body.Locals.Count > 0) {
            var sigBlobHandle = EncodeSig(b => {
                var sigEnc = b.LocalVariableSignature(body.Locals.Count);
                foreach (var localVar in body.Locals) {
                    var typeEnc = sigEnc.AddVariable().Type(false, localVar.IsPinned);
                    EncodeType(typeEnc, localVar.Type);
                }
            });
            localVarSigs = _builder.AddStandaloneSignature(sigBlobHandle);
        }
        int codeSize = body.Instructions[^1].GetEndOffset();
        var enc = _bodyEncoder.AddMethodBody(
            codeSize, body.MaxStack,
            body.ExceptionRegions.Count,
            hasSmallExceptionRegions: false, //TODO
            localVarSigs, attribs
        );
        EncodeInsts(body, new BlobWriter(enc.Instructions));

        //Add exception regions
        foreach (var ehr in body.ExceptionRegions) {
            enc.ExceptionRegions.Add(
                kind: ehr.Kind,
                tryOffset: ehr.TryStart,
                tryLength: ehr.TryEnd - ehr.TryStart,
                handlerOffset: ehr.HandlerStart,
                handlerLength: ehr.HandlerEnd - ehr.HandlerStart,
                catchType: ehr.CatchType == null ? default : GetHandle(ehr.CatchType),
                filterOffset: ehr.FilterStart
            );
        }
        return enc.Offset;
    }

    private void EncodeInsts(ILMethodBody body, BlobWriter writer)
    {
        foreach (ref var inst in body.Instructions.AsSpan()) {
            EncodeInst(ref writer, ref inst);
        }
    }

    private void EncodeInst(ref BlobWriter bw, ref ILInstruction inst)
    {
        int code = (int)inst.OpCode;
        if ((code & 0xFF00) == 0xFE00) {
            bw.WriteByte((byte)(code >> 8));
        }
        bw.WriteByte((byte)code);

        switch (inst.OpCode.GetOperandType()) {
            case ILOperandType.BrTarget: {
                bw.WriteInt32((int)inst.Operand! - inst.GetEndOffset());
                break;
            }
            case ILOperandType.Field:
            case ILOperandType.Method:
            case ILOperandType.Tok: {
                var handle = GetHandle((Entity)inst.Operand!);
                bw.WriteInt32(MetadataTokens.GetToken(handle));
                break;
            }
            case ILOperandType.Sig: {
                var handle = GetStandaloneSig((FuncPtrType)inst.Operand!);
                bw.WriteInt32(MetadataTokens.GetToken(handle));
                break;
            }
            case ILOperandType.Type: {
                var handle = GetTypeHandle((TypeDesc)inst.Operand!);
                bw.WriteInt32(MetadataTokens.GetToken(handle));
                break;
            }
            case ILOperandType.String: {
                var handle = _builder.GetOrAddUserString((string)inst.Operand!);
                bw.WriteInt32(MetadataTokens.GetToken(handle));
                break;
            }
            case ILOperandType.I: {
                bw.WriteInt32((int)inst.Operand!);
                break;
            }
            case ILOperandType.I8: {
                bw.WriteInt64((long)inst.Operand!);
                break;
            }
            case ILOperandType.R: {
                bw.WriteDouble((double)inst.Operand!);
                break;
            }
            case ILOperandType.Switch: {
                WriteTumpTable(ref bw, ref inst);
                break;
            }
            case ILOperandType.Var: {
                int varIndex = (int)inst.Operand!;
                Assert(varIndex == (ushort)varIndex);
                bw.WriteUInt16((ushort)varIndex);
                break;
            }
            case ILOperandType.ShortBrTarget: {
                int offset = (int)inst.Operand! - inst.GetEndOffset();
                Assert(offset == (sbyte)offset);
                bw.WriteSByte((sbyte)offset);
                break;
            }
            case ILOperandType.ShortI: {
                int value = (int)inst.Operand!;
                Assert(value == (sbyte)value);
                bw.WriteSByte((sbyte)value);
                break;
            }
            case ILOperandType.ShortR: {
                bw.WriteSingle((float)inst.Operand!);
                break;
            }
            case ILOperandType.ShortVar: {
                int varIndex = (int)inst.Operand!;
                Assert(varIndex == (byte)varIndex);
                bw.WriteByte((byte)varIndex);
                break;
            }
            default: {
                Assert(inst.Operand == null);
                break;
            }
        }
        static void WriteTumpTable(ref BlobWriter bw, ref ILInstruction inst)
        {
            int baseOffset = inst.GetEndOffset();
            var targets = (int[])inst.Operand!;

            bw.WriteInt32(targets.Length);
            for (int i = 0; i < targets.Length; i++) {
                bw.WriteInt32(targets[i] - baseOffset);
            }
        }
    }

    private BlobHandle EncodeMethodSig(MethodDef method)
    {
        return EncodeSig(b => {
            //TODO: callconv
            var pars = method.StaticParams;
            b.MethodSignature(default, method.GenericParams.Length, method.IsInstance)
                .Parameters(pars.Length, out var retTypeEnc, out var parsEnc);

            EncodeType(retTypeEnc.Type(), method.ReturnType);
            foreach (var par in pars) {
                var parEnc = parsEnc.AddParameter();
                EncodeType(parEnc.Type(), par.Type);
            }
        });
    }
    private BlobHandle EncodeMethodSig(MethodSpec method)
    {
        return EncodeSig(b => {
            var genArgEnc = b.MethodSpecificationSignature(method.GenericParams.Length);
            
            foreach (var par in method.GenericParams) {
                var parEnc = genArgEnc.AddArgument();
                EncodeType(parEnc, par);
            }
        });
    }

    private BlobHandle EncodePropertySig(PropertyDef prop)
    {
        return EncodeSig(b => {
            b.PropertySignature(prop.IsInstance)
                .Parameters(prop.ParamTypes.Count, out var retTypeEnc, out var parsEnc);

            EncodeType(retTypeEnc.Type(), prop.Type);
            foreach (var par in prop.ParamTypes) {
                var parEnc = parsEnc.AddParameter();
                EncodeType(parEnc.Type(), par);
            }
        });
    }

    private BlobHandle EncodeFieldSig(FieldDefOrSpec field)
    {
        return EncodeSig(b => EncodeType(b.FieldSignature(), field.Type));
    }

    private void EncodeType(SignatureTypeEncoder enc, TypeDesc type)
    {
        //Bypassing the encoder api because it's kinda weird and inconsistent.
        //https://github.com/dotnet/runtime/blob/1ba0394d71a4ea6bee7f6b28a22d666b7b56f913/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/Ecma335/Encoding/BlobEncoders.cs#L809
        switch (type) {
            case PrimType t: {
                var code = type.Kind switch {
                    #pragma warning disable format
                    TypeKind.Void   => SignatureTypeCode.Void,
                    TypeKind.Bool   => SignatureTypeCode.Boolean,
                    TypeKind.Char   => SignatureTypeCode.Char,
                    TypeKind.SByte  => SignatureTypeCode.SByte,
                    TypeKind.Byte   => SignatureTypeCode.Byte,
                    TypeKind.Int16  => SignatureTypeCode.Int16,
                    TypeKind.UInt16 => SignatureTypeCode.UInt16,
                    TypeKind.Int32  => SignatureTypeCode.Int32,
                    TypeKind.UInt32 => SignatureTypeCode.UInt32,
                    TypeKind.Int64  => SignatureTypeCode.Int64,
                    TypeKind.UInt64 => SignatureTypeCode.UInt64,
                    TypeKind.Single => SignatureTypeCode.Single,
                    TypeKind.Double => SignatureTypeCode.Double,
                    TypeKind.IntPtr => SignatureTypeCode.IntPtr,
                    TypeKind.UIntPtr => SignatureTypeCode.UIntPtr,
                    TypeKind.String => SignatureTypeCode.String,
                    TypeKind.Object => SignatureTypeCode.Object,
                    _ => throw new NotSupportedException()
                    #pragma warning restore format
                };
                enc.Builder.WriteByte((byte)code);
                break;
            }
            case ArrayType t: {
                EncodeType(enc.SZArray(), t.ElemType);
                break;
            }
            case MDArrayType t: {
                enc.Array(
                    e => EncodeType(e, t.ElemType),
                    s => s.Shape(t.Rank, t.Sizes, t.LowerBounds)
                );
                break;
            }
            case PointerType t: {
                EncodeType(enc.Pointer(), t.ElemType);
                break;
            }
            case ByrefType t: {
                enc.Builder.WriteByte((byte)SignatureTypeCode.ByReference);
                EncodeType(enc, t.ElemType);
                break;
            }
            case TypeDef t: {
                enc.Type(GetHandle(t), t.IsValueType);
                break;
            }
            case TypeSpec t: {
                var argEnc = enc.GenericInstantiation(GetHandle(t.Definition), t.GenericParams.Length, t.IsValueType);
                foreach (var arg in t.GenericParams) {
                    EncodeType(argEnc.AddArgument(), arg);
                }
                break;
            }
            case FuncPtrType t: {
                var attrs =
                    (t.HasExplicitThis ? FunctionPointerAttributes.HasExplicitThis : 0) |
                    (t.IsInstance ? FunctionPointerAttributes.HasThis : 0);

                var sigEnc = enc.FunctionPointer((SignatureCallingConvention)t.CallConv, attrs);
                sigEnc.Parameters(t.ArgTypes.Length, out var retTypeEnc, out var paramsEnc);

                EncodeType(retTypeEnc.Type(), t.ReturnType);

                foreach (var argType in t.ArgTypes) {
                    var paramEnc = paramsEnc.AddParameter();
                    EncodeType(paramEnc.Type(), argType);
                }
                break;
            }
            case GenericParamType t: {
                if (t.IsMethodParam) {
                    enc.GenericMethodTypeParameter(t.Index);
                } else {
                    enc.GenericTypeParameter(t.Index);
                }
                break;
            }
            default: throw new NotImplementedException();
        }
    }

    private BlobHandle EncodeSig(Action<BlobEncoder> encode)
    {
        var builder = new BlobBuilder();
        var encoder = new BlobEncoder(builder);
        encode(encoder);
        return _builder.GetOrAddBlob(builder);
    }

    private StringHandle AddString(string? str)
    {
        return str == null ? default : _builder.GetOrAddString(str);
    }
    private BlobHandle AddBlob(byte[]? data)
    {
        return data == null ? default : _builder.GetOrAddBlob(data);
    }

    private void SerializePE(BlobBuilder peBlob)
    {
        var header = new PEHeaderBuilder();

        var entryPoint = _mod.EntryPoint;

        var peBuilder = new ManagedPEBuilder(
            header: header,
            metadataRootBuilder: new MetadataRootBuilder(_builder),
            ilStream: _bodyEncoder.Builder,
            mappedFieldData: _fieldDataStream,
            entryPoint: entryPoint == null ? default : (MethodDefinitionHandle)_handleMap[entryPoint]
        );
        peBuilder.Serialize(peBlob);
    }
}