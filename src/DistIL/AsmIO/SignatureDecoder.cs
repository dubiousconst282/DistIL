namespace DistIL.AsmIO;

using System.Reflection.Metadata;

// II.23.2 Blobs and signatures
internal struct SignatureDecoder
{
    readonly ModuleLoader _loader;
    readonly GenericContext _genCtx;
    public BlobReader Reader;

    public SignatureDecoder(ModuleLoader loader, BlobHandle handle, GenericContext genCtx = default)
        : this(loader, loader._reader.GetBlobReader(handle), genCtx) { }
        
    public SignatureDecoder(ModuleLoader loader, BlobReader reader, GenericContext genCtx = default)
    {
        _loader = loader;
        _genCtx = genCtx;
        Reader = reader;
    }

    public TypeDesc DecodeType()
    {
        var code = Reader.ReadSignatureTypeCode();

        switch (code) {
            case >= SignatureTypeCode.Void and <= SignatureTypeCode.String:
            case SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr:
            case SignatureTypeCode.Object:
            case SignatureTypeCode.TypedReference: {
                return PrimType.GetFromSrmCode((PrimitiveTypeCode)code);
            }
            case SignatureTypeCode.Pointer: {
                return DecodeType().CreatePointer();
            }
            case SignatureTypeCode.ByReference: {
                return DecodeType().CreateByref();
            }
            case SignatureTypeCode.SZArray: {
                return DecodeType().CreateArray();
            }
            case SignatureTypeCode.Array: {
                var elemType = DecodeType();
                int rank = Reader.ReadCompressedInteger();
                var sizes = ReadIntArray(false);
                var lowerBounds = ReadIntArray(true);
                return new MDArrayType(elemType, rank, lowerBounds, sizes);
            }
            case SignatureTypeCode.FunctionPointer: {
                var sig = DecodeMethodSig();
                return new FuncPtrType(sig);
            }
            case SignatureTypeCode.GenericTypeInstance: {
                var typeDef = (TypeDef)DecodeType();
                var typeArgs = DecodeGenArgs();
                return typeDef.GetSpec(typeArgs);
            }
            case SignatureTypeCode.GenericTypeParameter:
            case SignatureTypeCode.GenericMethodParameter: {
                int index = Reader.ReadCompressedInteger();
                var isMethodParam = code == SignatureTypeCode.GenericMethodParameter;
                return _genCtx.GetArgument(index, isMethodParam)
                    ?? GenericParamType.GetUnbound(index, isMethodParam);
            }
            case SignatureTypeCode.TypeHandle: {
                var handle = Reader.ReadTypeHandle();
                Debug.Assert(handle.Kind != HandleKind.TypeSpecification || _genCtx.IsNull);
                return (TypeDesc)_loader.GetEntity(handle);
            }
            default:
                throw new NotSupportedException();
        }
    }

    public ImmutableArray<TypeDesc> DecodeGenArgs()
    {
        int count = Reader.ReadCompressedInteger();
        var builder = ImmutableArray.CreateBuilder<TypeDesc>(count);
        Debug.Assert(count > 0);

        for (int i = 0; i < count; i++) {
            builder.Add(DecodeType());
        }
        return builder.MoveToImmutable();
    }

    private ImmutableArray<int> ReadIntArray(bool signed)
    {
        int count = Reader.ReadCompressedInteger();
        if (count == 0) {
            return ImmutableArray<int>.Empty;
        }
        var builder = ImmutableArray.CreateBuilder<int>(count);
        for (int i = 0; i < count; i++) {
            builder.Add(signed ? Reader.ReadCompressedSignedInteger() : Reader.ReadCompressedInteger());
        }
        return builder.MoveToImmutable();
    }

    public MethodSig DecodeMethodSig()
    {
        var header = Reader.ReadSignatureHeader();
        int numGenPars = header.IsGeneric ? Reader.ReadCompressedInteger() : 0;
        var paramTypes = new TypeSig[Reader.ReadCompressedInteger()];
        var retType = DecodeTypeSig();

        for (int i = 0; i < paramTypes.Length; i++) {
            paramTypes[i] = DecodeTypeSig();
        }
        return new MethodSig(retType, paramTypes, header, numGenPars);
    }

    public TypeSig DecodeTypeSig()
    {
        var customMods = DecodeCustomMods();
        var type = DecodeType();
        return new TypeSig(type, customMods);
    }

    public ILVariable[] DecodeLocals()
    {
        ExpectHeader(SignatureKind.LocalVariables);

        var vars = new ILVariable[Reader.ReadCompressedInteger()];

        for (int i = 0; i < vars.Length; i++) {
            DecodeCustomMods(); // Custom modifiers on local vars are pretty useless, skip them
            bool isPinned = Reader.ReadSignatureTypeCode() == SignatureTypeCode.Pinned;
            if (!isPinned) {
                Reader.Offset--;
            }
            var type = DecodeType();

            vars[i] = new ILVariable(type, i, isPinned);
        }
        return vars;
    }

    private ImmutableArray<TypeModifier> DecodeCustomMods()
    {
        var modifiers = ImmutableArray<TypeModifier>.Empty;

        while (true) {
            var typeCode = Reader.ReadSignatureTypeCode();
            if (typeCode is not (SignatureTypeCode.RequiredModifier or SignatureTypeCode.OptionalModifier)) {
                Reader.Offset--;
                return modifiers;
            }
            var modifierType = (TypeDesc)_loader.GetEntity(Reader.ReadTypeHandle());
            bool isRequired = typeCode == SignatureTypeCode.RequiredModifier;

            // We don't expect many custom mods, this should be fine in most cases; otherwise, surprise O(n^2) slowdown!
            modifiers = modifiers.Add(new(modifierType.GetSpec(_genCtx), isRequired));
        }
    }

    public void ExpectHeader(SignatureKind kind)
    {
        var header = Reader.ReadSignatureHeader();
        if (header.Kind != kind) {
            throw new BadImageFormatException();
        }
    }
}