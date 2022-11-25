namespace DistIL.AsmIO;

public readonly struct GenericContext
{
    public static readonly GenericContext Empty = new(Array.Empty<TypeDesc>(), Array.Empty<TypeDesc>());

    public IReadOnlyList<TypeDesc> TypeArgs { get; }
    public IReadOnlyList<TypeDesc> MethodArgs { get; }

    public bool IsNull => TypeArgs == null && MethodArgs == null;

    public GenericContext(IReadOnlyList<TypeDesc>? typeArgs = null, IReadOnlyList<TypeDesc>? methodArgs = null)
    {
        Ensure.That(typeArgs != null || methodArgs != null, "Either `typeArgs` or `methodArgs` must be non-null");
        TypeArgs = typeArgs ?? Array.Empty<TypeDesc>();
        MethodArgs = methodArgs ?? Array.Empty<TypeDesc>();
    }
    public GenericContext(TypeDesc genType)
    {
        TypeArgs = genType.GenericParams;
        MethodArgs = Array.Empty<TypeDesc>();
    }
    public GenericContext(MethodDefOrSpec method)
    {
        TypeArgs = method.DeclaringType.GenericParams;
        MethodArgs = method.GenericParams;
    }

    public ImmutableArray<TypeDesc> FillParams(IReadOnlyCollection<TypeDesc> pars)
    {
        var builder = ImmutableArray.CreateBuilder<TypeDesc>(pars.Count);
        foreach (var type in pars) {
            builder.Add(type.GetSpec(this));
        }
        return builder.MoveToImmutable();
    }

    public TypeDesc? GetArgument(int index, bool isMethodParam)
    {
        var args = isMethodParam ? MethodArgs : TypeArgs;
        return (args != null && index < args.Count) ? args[index] : null;
    }
}
