namespace DistIL.IR.Intrinsics;

public class IRIntrinsic : IntrinsicDesc
{
    public override string Namespace => "DistIL";
    public override string Name => Id.ToString();

    public IRIntrinsicId Id { get; private init; }

    public static readonly IRIntrinsic
        //void Marker(string? str)      NOP used for debugging
        Marker = new() {
            Id = IRIntrinsicId.Marker,
            ParamTypes = ImmutableArray.Create<TypeDesc>(PrimType.String),
            ReturnType = PrimType.Void
        },
        //T CopyDef<T>(T value)         Creates a SSA copy for use by the RemovePhis pass.
        CopyDef = new() {
            Id = IRIntrinsicId.CopyDef,
            ParamTypes = ImmutableArray.Create(s_AnyType),
            ReturnType = s_Typeof0
        };
}
public enum IRIntrinsicId
{
    Marker,
    CopyDef
}