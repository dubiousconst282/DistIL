namespace DistIL.AsmIO;

using System.Reflection.Metadata;
using System.Text;

using DistIL.IR;

public class FuncPtrType : TypeDesc
{
    public TypeDesc ReturnType { get; }
    public ImmutableArray<TypeDesc> ArgTypes { get; }
    public CallConvention CallConv { get; }

    public bool IsInstance { get; }
    public bool HasExplicitThis { get; }

    public override TypeKind Kind => TypeKind.Pointer;
    public override StackType StackType => StackType.NInt;
    public override TypeDesc? BaseType => PrimType.ValueType;
    public override bool IsValueType => true;

    public override string? Namespace => "";
    public override string Name => ToString();

    public FuncPtrType(TypeDesc retType, ImmutableArray<TypeDesc> argTypes, CallConvention callConv, bool isInstance = false, bool explicitThis = false)
    {
        ReturnType = retType;
        ArgTypes = argTypes;
        CallConv = callConv;
        IsInstance = isInstance;
        HasExplicitThis = explicitThis;
    }

    public FuncPtrType(MethodDesc desc, CallConvention callConv = CallConvention.Managed)
    {
        ReturnType = desc.ReturnType;
        ArgTypes = desc.Params.Select(p => p.Type).ToImmutableArray();
        CallConv = callConv;
        IsInstance = desc.IsInstance;
    }

    internal FuncPtrType(MethodSignature<TypeDesc> sig)
    {
        Ensure(sig.GenericParameterCount == 0);
        var header = sig.Header;
        ReturnType = sig.ReturnType;
        ArgTypes = sig.ParameterTypes;
        CallConv = (CallConvention)header.CallingConvention;
        IsInstance = header.IsInstance;
        HasExplicitThis = header.HasExplicitThis;
    }

    public override void Print(PrintContext ctx, bool includeNs = true)
    {
        ctx.Print($"delegate* ", PrintToner.Keyword);
        ctx.Print(CallConv.ToString().ToLower());
        ctx.PrintSequence("<", ">", ArgTypes, p => p.Print(ctx, includeNs));
    }

    public override bool Equals(TypeDesc? other)
        => other is FuncPtrType o && o.ReturnType == ReturnType && o.CallConv == CallConv &&
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