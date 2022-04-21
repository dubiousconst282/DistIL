namespace DistIL.IR;

using System.Text;

public class FuncPtrType : RType
{
    public RType RetType { get; }
    public ImmutableArray<RType> ArgTypes { get; }
    public CallConvention CallConv { get; }

    public bool IsInstance { get; }
    public bool HasExplicitThis { get; }

    public override TypeKind Kind => TypeKind.Pointer;
    public override StackType StackType => StackType.NInt;

    public override string? Namespace => "";
    public override string Name => ToString();

    public FuncPtrType(RType retType, ImmutableArray<RType> argTypes, CallConvention callConv, bool isInstance = false, bool explicitThis = false)
    {
        RetType = retType;
        ArgTypes = argTypes;
        CallConv = callConv;
        IsInstance = isInstance;
        HasExplicitThis = explicitThis;
    }

    public override void Print(StringBuilder sb)
    {
        sb.Append($"delegate* {CallConv.ToString().ToLower()}<");
        sb.AppendJoin(", ", ArgTypes.Append(RetType));
        sb.Append(">");
    }

    public override bool Equals(RType? other)
        => other is FuncPtrType o && o.RetType == RetType && o.CallConv == CallConv &&
           o.IsInstance == IsInstance && o.IsGeneric == IsGeneric &&
           o.ArgTypes.SequenceEqual(ArgTypes);
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