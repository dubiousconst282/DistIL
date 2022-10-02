namespace DistIL.AsmIO;

public struct GenericContext
{
    public IReadOnlyList<TypeDesc> TypeArgs { get; }
    public IReadOnlyList<TypeDesc> MethodArgs { get; }

    public GenericContext(IReadOnlyList<TypeDesc>? typeArgs = null, IReadOnlyList<TypeDesc>? methodArgs = null)
    {
        Ensure(typeArgs != null || methodArgs != null, "Either `typeArgs` or `methodArgs` must be non-null");
        TypeArgs = typeArgs ?? Array.Empty<TypeDesc>();
        MethodArgs = methodArgs ?? Array.Empty<TypeDesc>();
    }
    public GenericContext(TypeDefOrSpec type)
    {
        TypeArgs = type.GenericParams;
        MethodArgs = Array.Empty<TypeDesc>();
    }
    public GenericContext(MethodDefOrSpec method)
    {
        TypeArgs = method.DeclaringType.GenericParams;
        MethodArgs = method.GenericParams;
    }

    public ImmutableArray<TypeDesc> FillParams(ImmutableArray<TypeDesc> pars)
    {
        var builder = ImmutableArray.CreateBuilder<TypeDesc>(pars.Length);
        foreach (var type in pars) {
            builder.Add(type.GetSpec(this));
        }
        return builder.MoveToImmutable();
    }
}
