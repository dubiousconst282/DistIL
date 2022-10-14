namespace DistIL.AsmIO;

using System.Reflection.Metadata;

/// <summary> Represents the signature of a method declared in a type. </summary>
public readonly struct MethodSig
{
    public TypeDesc ReturnType { get; }
    public IReadOnlyList<TypeDesc> ParamTypes { get; }
    public int NumGenericParams { get; }
    public bool? IsInstance { get; }

    internal MethodSig(in MethodSignature<TypeDesc> srmSig)
    {
        ReturnType = srmSig.ReturnType;
        ParamTypes = srmSig.ParameterTypes;
        NumGenericParams = srmSig.GenericParameterCount;
        IsInstance = srmSig.Header.IsInstance;
    }

    /// <remarks> Note that <paramref name="paramTypes"/> should not include the instance type (`this` parameter). </remarks>
    public MethodSig(TypeDesc retType, IReadOnlyList<TypeDesc> paramTypes, bool? isInstance = null, int numGenPars = 0)
    {
        ReturnType = retType;
        ParamTypes = paramTypes;
        NumGenericParams = numGenPars;
        IsInstance = isInstance;
    }

    public bool Matches(MethodDesc method, in GenericContext spec)
    {
        return method.GenericParams.Length == NumGenericParams &&
            (IsInstance == null || IsInstance == method.IsInstance) &&
            method.ReturnType.GetSpec(spec) == ReturnType &&
            CompareParams(method, spec);
    }

    private bool CompareParams(MethodDesc method, in GenericContext spec)
    {
        var pars1 = method.StaticParams;
        var pars2 = ParamTypes;

        if (pars1.Length != pars2.Count) {
            return false;
        }
        for (int i = 0; i < pars1.Length; i++) {
            var type1 = pars1[i].Type.GetSpec(spec);
            var type2 = pars2[i];
            if (type1 != type2) {
                return false;
            }
        }
        return true;
    }
}
