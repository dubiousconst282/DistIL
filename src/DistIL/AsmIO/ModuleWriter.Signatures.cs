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
        //Bypassing the encoder api because it's so goddamn awful, even though we'll probably get bitten sooner or later.
        //https://github.com/dotnet/runtime/blob/1ba0394d71a4ea6bee7f6b28a22d666b7b56f913/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/Ecma335/Encoding/BlobEncoders.cs#L809
        switch (sig.Type) {
            case PrimType t: {
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
                var argEnc = enc.GenericInstantiation(GetHandle(t.Definition), t.GenericParams.Length, t.IsValueType);
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

    private BlobHandle EncodeMethodSig(MethodSig sig)
    {
        return EncodeSig(b => {
            var pars = sig.ParamTypes;
            b.MethodSignature(
                (SignatureCallingConvention)sig.CallConv, sig.NumGenericParams,
                sig.IsInstance ?? throw new InvalidOperationException()
            ).Parameters(pars.Count, out var retTypeEnc, out var parsEnc);

            EncodeType(retTypeEnc.Type(), sig.ReturnType);

            foreach (var par in pars) {
                EncodeType(parsEnc.AddParameter().Type(), par.Type);
            }
        });
    }

    private BlobHandle EncodeMethodSig(MethodDesc method)
    {
        return EncodeSig(b => {
            var pars = method.StaticParams;
            b.MethodSignature(default, method.GenericParams.Length, method.IsInstance)
                .Parameters(pars.Length, out var retTypeEnc, out var parsEnc);

            EncodeType(retTypeEnc.Type(), method.ReturnSig);

            foreach (var par in pars) {
                EncodeType(parsEnc.AddParameter().Type(), par.Type);
            }
        });
    }

    private BlobHandle EncodeMethodSpecSig(MethodSpec method)
    {
        return EncodeSig(b => {
            var genArgEnc = b.MethodSpecificationSignature(method.GenericParams.Length);

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