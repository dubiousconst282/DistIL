namespace DistIL.AsmIO;

public class FuncPtrType : TypeDesc
{
    public MethodSig Signature { get; }

    public override TypeKind Kind => TypeKind.Pointer;
    public override StackType StackType => StackType.NInt;
    public override TypeDesc? BaseType => PrimType.ValueType;
    public override bool IsValueType => true;
    public override bool IsUnboundGeneric => Signature.ReturnType.Type.IsUnboundGeneric || Signature.ParamTypes.Any(p => p.Type.IsUnboundGeneric);

    public override string? Namespace => "";
    public override string Name => ToString();

    public FuncPtrType(MethodSig sig)
    {
        Signature = sig;
    }

    public override void Print(PrintContext ctx, bool includeNs = false)
    {
        ctx.Print($"delegate* ", PrintToner.Keyword);
        Signature.Print(ctx, includeNs);
    }

    public override TypeDesc GetSpec(GenericContext context)
    {
        throw new NotImplementedException();
    }

    public override bool Equals(TypeDesc? other)
        => other is FuncPtrType o && o.Signature.Equals(Signature);
}