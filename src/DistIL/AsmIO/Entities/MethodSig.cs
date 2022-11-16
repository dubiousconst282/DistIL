namespace DistIL.AsmIO;

using System.Collections.Generic;
using System.Reflection.Metadata;

/// <summary> Represents the signature of a method declared in a type. </summary>
public readonly struct MethodSig : IEquatable<MethodSig>
{
    const SignatureAttributes kInvariantInstance = (SignatureAttributes)0x80;

    public TypeSig ReturnType { get; }
    public IReadOnlyList<TypeSig> ParamTypes { get; }

    public int NumGenericParams { get; }

    public bool? IsInstance => (_header.RawValue & (byte)kInvariantInstance) != 0 ? null : _header.IsInstance;
    public bool IsGeneric => _header.IsGeneric;
    public CallConvention CallConv => (CallConvention)_header.CallingConvention;

    readonly SignatureHeader _header;

    public bool IsNull => ReturnType == null!;

    /// <remarks> Note that <paramref name="paramTypes"/> should not include the instance type (`this` parameter). </remarks>
    public MethodSig(TypeSig retType, IReadOnlyList<TypeSig> paramTypes, bool? isInstance = null, int numGenPars = 0)
    {
        ReturnType = retType;
        ParamTypes = paramTypes;
        NumGenericParams = numGenPars;
        _header = new(SignatureKind.Method, default, isInstance == null ? (SignatureAttributes)0x80 : 0);
    }

    /// <remarks> Note that <paramref name="paramTypes"/> should not include the instance type (`this` parameter). </remarks>
    public MethodSig(TypeSig retType, IReadOnlyList<TypeSig> paramTypes, SignatureHeader header, int numGenPars = 0)
    {
        ReturnType = retType;
        ParamTypes = paramTypes;
        NumGenericParams = numGenPars;
        _header = header;
        Ensure.That(!header.Attributes.HasFlag(kInvariantInstance));
    }

    public bool Matches(MethodDesc method, in GenericContext spec)
    {
        return method.GenericParams.Length == NumGenericParams &&
            (IsInstance == null || IsInstance == method.IsInstance) &&
            method.ReturnSig.GetSpec(spec) == ReturnType &&
            CompareParams(method, spec);
    }

    private bool CompareParams(MethodDesc method, in GenericContext spec)
    {
        var pars1 = method.ParamSig;
        int offset = method.IsInstance ? 1 : 0;

        if ((pars1.Count - offset) != ParamTypes.Count) {
            return false;
        }
        for (int i = 0; i < ParamTypes.Count; i++) {
            var type1 = pars1[i + offset].GetSpec(spec);
            if (type1 != ParamTypes[i]) {
                return false;
            }
        }
        return true;
    }

    public bool Equals(MethodSig other)
    {
        throw new NotImplementedException();
    }
    public override bool Equals(object? obj)
    {
        throw new NotImplementedException();
    }
    public override int GetHashCode()
    {
        throw new NotImplementedException();
    }
}

//Copied from SignatureCallingConvention
/// <summary>
/// Specifies how arguments in a given signature are passed from the caller to the
/// callee. The underlying values of the fields in this type correspond to the representation
/// in the leading signature byte represented by a System.Reflection.Metadata.SignatureHeader
/// structure.
/// </summary>
public enum CallConvention : byte
{
    /// <summary> A managed calling convention with a fixed-length argument list. </summary>
    Managed = 0,
    /// <summary> An unmanaged C/C++ style calling convention where the call stack is cleaned by the caller. </summary>
    CDecl = 1,
    /// <summary> An unmanaged calling convention where the call stack is cleaned up by the callee. </summary>
    StdCall = 2,
    /// <summary> An unmanaged C++ style calling convention for calling instance member functions with a fixed argument list. </summary>
    ThisCall = 3,
    /// <summary> An unmanaged calling convention where arguments are passed in registers when possible. </summary>
    FastCall = 4,
    /// <summary> A managed calling convention for passing extra arguments. </summary>
    VarArgs = 5,
    /// <summary> Indicates that the specifics of the unmanaged calling convention are encoded as modopts. </summary>
    Unmanaged = 9
}