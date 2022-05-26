namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DistIL.IR;

internal class ModuleWriter
{
    readonly ModuleDef _mod;
    readonly MetadataBuilder _builder;
    readonly MethodBodyStreamEncoder _bodyEncoder;
    private BlobBuilder? _fieldDataStream;
    private BlobBuilder? _managedResourceStream;
    private MethodDefinitionHandle _entryPoint;

    private Dictionary<ModuleEntity, EntityHandle> _handleMap = new();

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
            (AssemblyHashAlgorithm)asmName.HashAlgorithm
        );
        _handleMap.Add(_mod, mainAsmHandle);

        AllocHandles();
        EmitTypes();

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

    private void EmitTypes()
    {
        foreach (var type in _mod.TypeDefs) {
            EmitType(type);
        }
    }

    private EntityHandle GetEntityHandle(ModuleEntity entity)
    {
        if (_handleMap.TryGetValue(entity, out var handle)) {
            return handle;
        }
        Assert(entity.Module != _mod); //all handles in _mod must be allocated first

        handle = entity switch {
            ModuleDef mod   => AddAsmRef(mod),
            TypeDef type    => AddTypeRef(type),
            MethodDef meth  => AddMethodRef(meth),
            FieldDef field  => AddFieldRef(field)
        };
        _handleMap[entity] = handle;
        return handle;
    }

    private EntityHandle GetTypeHandle(TypeDesc type)
    {
        return type switch {
            TypeDef def => GetEntityHandle(def)
        };
    }

    private EntityHandle AddAsmRef(ModuleDef module)
    {
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

    private EntityHandle AddTypeRef(TypeDef type)
    {
        var rootAsm = _mod._typeRefRoots.GetValueOrDefault(type, type.Module);
        return _builder.AddTypeReference(
            GetEntityHandle(rootAsm),
            AddString(type.Namespace),
            AddString(type.Name)
        );
    }

    private EntityHandle AddMethodRef(MethodDef meth)
    {
        var declType = GetEntityHandle(meth.DeclaringType);
        return _builder.AddMemberReference(
            declType,
            AddString(meth.Name),
            EmitMethodSig(meth)
        );
    }

    private EntityHandle AddFieldRef(FieldDef field)
    {
        var declType = GetEntityHandle(field.DeclaringType);
        return _builder.AddMemberReference(
            declType,
            AddString(field.Name),
            EmitFieldSig(field)
        );
    }

    private void AssertHandleAllocated(ModuleEntity def, EntityHandle handle)
    {
        Assert(_handleMap[def] == handle);
    }

    private void EmitType(TypeDef type)
    {
        var firstFieldHandle = MetadataTokens.FieldDefinitionHandle(_builder.GetRowCount(TableIndex.Field) + 1);
        var firstMethodHandle = MetadataTokens.MethodDefinitionHandle(_builder.GetRowCount(TableIndex.MethodDef) + 1);

        var handle = _builder.AddTypeDefinition(
            type.Attribs,
            AddString(type.Namespace),
            AddString(type.Name),
            type.BaseType == null ? default : GetTypeHandle(type.BaseType),
            firstFieldHandle,
            firstMethodHandle
        );
        AssertHandleAllocated(type, handle);
        Assert(type.Fields.Count == 0 || _handleMap[type.Fields[0]] == firstFieldHandle);
        Assert(type.Methods.Count == 0 || _handleMap[type.Methods[0]] == firstMethodHandle);

        if (type.IsNested) {
            _builder.AddNestedType(handle, (TypeDefinitionHandle)GetTypeHandle(type.DeclaringType));
        }
        foreach (var field in type.Fields) {
            EmitField(field);
        }
        foreach (var method in type.Methods) {
            EmitMethod(method);
        }
    }

    private void EmitField(FieldDef field)
    {
        var handle = _builder.AddFieldDefinition(
            field.Attribs,
            AddString(field.Name),
            EmitFieldSig(field)
        );
        AssertHandleAllocated(field, handle);

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
        var signature = EmitMethodSig(method);
        int bodyOffset = EmitBody(method.ILBody);
        var firstParamHandle = MetadataTokens.ParameterHandle(_builder.GetRowCount(TableIndex.Param) + 1);

        var pars = method.StaticParams;
        for (int i = 0; i < pars.Length; i++) {
            var par = pars[i];
            if (par.Name != null) {
                _builder.AddParameter(ParameterAttributes.None, AddString(par.Name), i + 1);
            }
        }

        var handle = _builder.AddMethodDefinition(
            method.Attribs,
            method.ImplAttribs,
            AddString(method.Name),
            signature, bodyOffset, firstParamHandle
        );
        AssertHandleAllocated(method, handle);
    }

    private int EmitBody(ILMethodBody? body)
    {
        if (body == null) {
            return -1;
        }
        var localVarSigs = default(StandaloneSignatureHandle);
        var attribs = body.InitLocals ? MethodBodyAttributes.InitLocals : 0;

        if (body.Locals.Count > 0) {
            var sigBlobHandle = EmitSig(b => {
                var sigEnc = b.LocalVariableSignature(body.Locals.Count);
                foreach (var localVar in body.Locals) {
                    var typeEnc = sigEnc.AddVariable().Type(false, localVar.IsPinned);
                    EncodeType(typeEnc, localVar.Type);
                }
            });
            localVarSigs = _builder.AddStandaloneSignature(sigBlobHandle);
        }
        var ilBytes = EncodeInsts(body);

        var enc = _bodyEncoder.AddMethodBody(
            ilBytes.Count, body.MaxStack,
            body.ExceptionRegions.Count,
            hasSmallExceptionRegions: false, //TODO
            localVarSigs, attribs
        );
        //Copy IL bytes to output blob
        new BlobWriter(enc.Instructions).WriteBytes(ilBytes);

        //Add exception regions
        foreach (var ehr in body.ExceptionRegions) {
            enc.ExceptionRegions.Add(
                kind: ehr.Kind,
                tryOffset: ehr.TryStart,
                tryLength: ehr.TryEnd - ehr.TryStart,
                handlerOffset: ehr.HandlerStart,
                handlerLength: ehr.HandlerEnd - ehr.HandlerStart,
                catchType: ehr.CatchType == null ? default : GetTypeHandle(ehr.CatchType),
                filterOffset: ehr.FilterStart
            );
        }
        return enc.Offset;
    }

    private BlobBuilder EncodeInsts(ILMethodBody body)
    {
        var blob = new BlobBuilder();
        foreach (ref var inst in body.Instructions.AsSpan()) {
            EncodeInst(blob, ref inst);
        }
        return blob;
    }

    private void EncodeInst(BlobBuilder bb, ref ILInstruction inst)
    {
        int code = (int)inst.OpCode;
        if ((code & 0xFF00) == 0xFE00) {
            bb.WriteByte((byte)(code >> 8));
        }
        bb.WriteByte((byte)code);

        switch (inst.OpCode.GetOperandType()) {
            case ILOperandType.BrTarget: {
                bb.WriteInt32((int)inst.Operand! - inst.GetEndOffset());
                break;
            }
            case ILOperandType.Field:
            case ILOperandType.Method:
            case ILOperandType.Sig:
            case ILOperandType.Tok: {
                var handle = GetEntityHandle((ModuleEntity)inst.Operand!);
                bb.WriteInt32(MetadataTokens.GetToken(handle));
                break;
            }
            case ILOperandType.Type: {
                var handle = AddTypeRef((TypeDef)inst.Operand!);
                bb.WriteInt32(MetadataTokens.GetToken(handle));
                break;
            }
            case ILOperandType.String:{
                var handle = _builder.GetOrAddUserString((string)inst.Operand!);
                bb.WriteInt32(MetadataTokens.GetToken(handle));
                break;
            }
            case ILOperandType.I: {
                bb.WriteInt32((int)inst.Operand!);
                break;
            }
            case ILOperandType.I8: {
                bb.WriteInt64((long)inst.Operand!);
                break;
            }
            case ILOperandType.R: {
                bb.WriteDouble((double)inst.Operand!);
                break;
            }
            case ILOperandType.Switch: {
                WriteTumpTable(bb, ref inst);
                break;
            }
            case ILOperandType.Var: {
                int varIndex = (int)inst.Operand!;
                Assert(varIndex == (ushort)varIndex);
                bb.WriteUInt16((ushort)varIndex);
                break;
            }
            case ILOperandType.ShortBrTarget: {
                int offset = (int)inst.Operand! - inst.GetEndOffset();
                Assert(offset == (sbyte)offset);
                bb.WriteSByte((sbyte)offset);
                break;
            }
            case ILOperandType.ShortI: {
                int value = (int)inst.Operand!;
                Assert(value == (sbyte)value);
                bb.WriteSByte((sbyte)value);
                break;
            }
            case ILOperandType.ShortR: {
                bb.WriteSingle((float)inst.Operand!);
                break;
            }
            case ILOperandType.ShortVar: {
                int varIndex = (int)inst.Operand!;
                Assert(varIndex == (byte)varIndex);
                bb.WriteByte((byte)varIndex);
                break;
            }
            default: {
                Assert(inst.Operand == null);
                break;
            }
        }
        static void WriteTumpTable(BlobBuilder bb, ref ILInstruction inst)
        {
            int baseOffset = inst.GetEndOffset();
            var targets = (int[])inst.Operand!;

            bb.WriteInt32(targets.Length);
            for (int i = 0; i < targets.Length; i++) {
                bb.WriteInt32(targets[i] - baseOffset);
            }
        }
    }

    private BlobHandle EmitMethodSig(MethodDef method)
    {
        return EmitSig(b => {
            //TODO: callconv, genericParamCount
            var pars = method.StaticParams;
            b.MethodSignature(isInstanceMethod: method.IsInstance)
                .Parameters(
                    pars.Length,
                    out var retTypeEnc, out var parsEnc
                );
            EncodeType(retTypeEnc.Type(), method.ReturnType);
            foreach (var arg in pars) {
                var parEnc = parsEnc.AddParameter();
                EncodeType(parEnc.Type(), arg.Type);
            }
        });
    }

    private BlobHandle EmitFieldSig(FieldDef field)
    {
        return EmitSig(b => EncodeType(b.FieldSignature(), field.Type));
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
                enc.Type(GetEntityHandle(t), t.IsValueType);
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
            default: throw new NotImplementedException();
        }
    }

    private BlobHandle EmitSig(Action<BlobEncoder> encode)
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
    {/*
        var hdrs = _mod.PE.PEHeaders;
        var peHdr = hdrs.PEHeader!;
        var coffHdr = hdrs.CoffHeader;
        var corHdr = hdrs.CorHeader;

        var header = new PEHeaderBuilder(
            machine: coffHdr.Machine,
            sectionAlignment: peHdr.SectionAlignment,
            fileAlignment: peHdr.FileAlignment,
            imageBase: peHdr.ImageBase,
            majorLinkerVersion: peHdr.MajorLinkerVersion,
            minorLinkerVersion: peHdr.MinorLinkerVersion,
            majorOperatingSystemVersion: peHdr.MajorOperatingSystemVersion,
            minorOperatingSystemVersion: peHdr.MinorOperatingSystemVersion,
            majorImageVersion: peHdr.MajorImageVersion,
            minorImageVersion: peHdr.MinorImageVersion,
            majorSubsystemVersion: peHdr.MajorSubsystemVersion,
            minorSubsystemVersion: peHdr.MinorSubsystemVersion,
            subsystem: peHdr.Subsystem,
            dllCharacteristics: peHdr.DllCharacteristics,
            imageCharacteristics: coffHdr.Characteristics,
            sizeOfStackReserve: peHdr.SizeOfStackReserve,
            sizeOfStackCommit: peHdr.SizeOfStackCommit,
            sizeOfHeapReserve: peHdr.SizeOfHeapReserve,
            sizeOfHeapCommit: peHdr.SizeOfHeapCommit
        );

        var entryPoint = _mod.EntryPoint;

        var peBuilder = new ManagedPEBuilder(
            header: header,
            metadataRootBuilder: new MetadataRootBuilder(_builder),
            ilStream: _bodyEncoder.Builder,
            mappedFieldData: _fieldDataStream,
            managedResources: _managedResourceStream,
            entryPoint: entryPoint == null ? default : (MethodDefinitionHandle)_handleMap[entryPoint]
        );
        peBuilder.Serialize(peBlob);*/
    }
}