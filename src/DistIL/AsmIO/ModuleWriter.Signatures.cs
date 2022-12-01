namespace DistIL.AsmIO;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

partial class ModuleWriter
{
    private void EncodeType(SignatureTypeEncoder enc, TypeSig sig)
    {
        foreach (var mod in sig.CustomMods) {
            enc.CustomModifiers().AddModifier(GetHandle(mod.Type), !mod.IsRequired);
        }
        switch (sig.Type) {
            case PrimType t: {
                if (ReferenceEquals(t, PrimType.Array)) { //PrimTypes were a mistake...
                    enc.Type(GetHandle(_mod.Resolver.SysTypes.Array), false);
                    return;
                }
                enc.Builder.WriteByte((byte)t.Kind.ToSrmTypeCode());
                break;
            }
            case ArrayType t: {
                EncodeType(enc.SZArray(), t.ElemType);
                break;
            }
            case MDArrayType t: {
                enc.Array(out var elemTypeEnc, out var shapeEnc);
                EncodeType(elemTypeEnc, t.ElemType);
                shapeEnc.Shape(t.Rank, t.Sizes, t.LowerBounds);
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
                var argEnc = enc.GenericInstantiation(GetHandle(t.Definition), t.GenericParams.Count, t.IsValueType);
                foreach (var arg in t.GenericParams) {
                    EncodeType(argEnc.AddArgument(), arg);
                }
                break;
            }
            case FuncPtrType t: {
                EncodeMethodSig(t.Signature);
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

    private BlobHandle EncodeMethodSig(MethodSig sig, bool isPropSig = false)
    {
        return EncodeSig(b => {
            var pars = sig.ParamTypes;
            bool isInstance = sig.IsInstance ?? throw new InvalidOperationException();

            var sigEnc = isPropSig
                ? b.PropertySignature(isInstance)
                : b.MethodSignature((SignatureCallingConvention)sig.CallConv, sig.NumGenericParams, isInstance);

            sigEnc.Parameters(pars.Count, out var retTypeEnc, out var parsEnc);

            EncodeType(retTypeEnc.Type(), sig.ReturnType);

            foreach (var par in pars) {
                EncodeType(parsEnc.AddParameter().Type(), par.Type);
            }
        });
    }

    private BlobHandle EncodeMethodSig(MethodDesc method)
    {
        return EncodeSig(b => {
            var pars = method.ParamSig;
            int offset = method.IsInstance ? 1 : 0;

            b.MethodSignature(default, method.GenericParams.Count, method.IsInstance)
                .Parameters(pars.Count - offset, out var retTypeEnc, out var parsEnc);

            EncodeType(retTypeEnc.Type(), method.ReturnSig);

            for (int i = offset; i < pars.Count; i++) {
                EncodeType(parsEnc.AddParameter().Type(), pars[i]);
            }
        });
    }

    private BlobHandle EncodeMethodSpecSig(MethodSpec method)
    {
        return EncodeSig(b => {
            var genArgEnc = b.MethodSpecificationSignature(method.GenericParams.Count);

            foreach (var par in method.GenericParams) {
                EncodeType(genArgEnc.AddArgument(), par);
            }
        });
    }

    private BlobHandle EncodeFieldSig(FieldDef field)
    {
        return EncodeSig(b => EncodeType(b.FieldSignature(), field.Type));
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
}