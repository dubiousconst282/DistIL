namespace DistIL.AsmIO;

using System.Reflection.Metadata;

public readonly struct MethodSig
{
    public TypeDesc ReturnType { get; }
    public IReadOnlyList<TypeDesc> ParamTypes { get; }
    public int NumGenericParams { get; }
    public bool IsInstance => _hdr.IsInstance;

    readonly SignatureHeader _hdr;

    public MethodSig(in MethodSignature<TypeDesc> srmSig)
    {
        ReturnType = srmSig.ReturnType;
        ParamTypes = srmSig.ParameterTypes;
        NumGenericParams = srmSig.GenericParameterCount;
        _hdr = srmSig.Header;
    }

    public MethodSig(TypeDesc retType, params TypeDesc[] paramTypes)
    {
        ReturnType = retType;
        ParamTypes = paramTypes;
        NumGenericParams = 0;
    }

    public MethodSig(TypeDesc retType, IReadOnlyList<TypeDesc> paramTypes, int numGenPars = 0)
    {
        ReturnType = retType;
        ParamTypes = paramTypes;
        NumGenericParams = numGenPars;
    }

    public bool Equals(MethodDesc method)
    {
        if (method.ReturnType != ReturnType) {
            return false;
        }
        if (NumGenericParams != method.GenericParams.Length) {
            return false;
        }
        var p1 = method.StaticParams;
        var p2 = ParamTypes;
        if (p1.Length != p2.Count) {
            return false;
        }
        for (int i = 0; i < p1.Length; i++) {
            if (p1[i].Type != p2[i]) {
                return false;
            }
        }
        return true;
    }
}
