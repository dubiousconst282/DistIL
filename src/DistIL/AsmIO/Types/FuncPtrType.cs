namespace DistIL.AsmIO;

public class FuncPtrType : TypeDesc
{
    public MethodSig Signature { get; }

    public override TypeKind Kind => TypeKind.Pointer;
    public override StackType StackType => StackType.NInt;
    public override TypeDesc? BaseType => PrimType.ValueType;
    public override bool IsValueType => true;

    public override string? Namespace => "";
    public override string Name => ToString();

    public FuncPtrType(MethodSig sig)
    {
        Signature = sig;
    }

    public override void Print(PrintContext ctx, bool includeNs = false)
    {
        ctx.Print($"delegate* ", PrintToner.Keyword);
        ctx.Print(Signature.CallConv.ToString().ToLower());
        ctx.PrintSequence("(", ")", Signature.ParamTypes, p => p.Print(ctx, includeNs));
    }

    public override bool Equals(TypeDesc? other)
        => other is FuncPtrType o && o.Signature.Equals(Signature);
}