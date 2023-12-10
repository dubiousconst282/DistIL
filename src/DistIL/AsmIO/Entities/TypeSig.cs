namespace DistIL.AsmIO;

using System.Runtime.CompilerServices;

// TODO: consider renaming this to SigType?

/// <summary> Represents a type used by a signature, which may contain modifiers. </summary>
public readonly struct TypeSig : IEquatable<TypeSig>, IPrintable
{
    // Modified types are quite rare. We can save a little bit of memory by heap allocating
    // them into a wrapper class instead of always having a field for the modifier array.
    readonly object _data; // TypeDesc | TypeAndMods

    public TypeDesc Type
        => (_data as TypeAndMods)?.Type ?? Unsafe.As<TypeDesc>(_data);

    /// <remarks> Note: generic modifiers are never specialized. </remarks>
    public ImmutableArray<TypeModifier> Modifiers
        => (_data as TypeAndMods)?.Mods ?? ImmutableArray<TypeModifier>.Empty;

    public bool HasModifiers => _data.GetType() == typeof(TypeAndMods);

    public TypeSig(TypeDesc type, ImmutableArray<TypeModifier> mods = default)
    {
        _data = mods.IsDefaultOrEmpty
            ? type
            : new TypeAndMods() { Type = type, Mods = mods };
    }

    public TypeSig GetSpec(GenericContext ctx)
    {
        return new TypeSig(Type.GetSpec(ctx), Modifiers);
    }

    public void Print(PrintContext ctx, bool includeNs = false)
    {
        Type.Print(ctx, includeNs);

        foreach (var (modType, isRequired) in Modifiers) {
            ctx.Print(isRequired ? " modreq" : " modopt", PrintToner.Keyword);
            ctx.Print("(");
            modType.Print(ctx, includeNs);
            ctx.Print(")");
        }
    }
    void IPrintable.Print(PrintContext ctx) => Print(ctx, false);
    void IPrintable.PrintAsOperand(PrintContext ctx) => Print(ctx, false);


    public bool Equals(TypeSig other) => other.Type == Type && Modifiers.SequenceEqual(other.Modifiers);

    public override bool Equals(object? obj) => obj is TypeSig sig && Equals(sig);
    public override int GetHashCode() => Type.GetHashCode();
    public override string ToString() => PrintContext.ToString(this);

    public static bool operator ==(TypeSig left, TypeSig right) => left.Equals(right);
    public static bool operator !=(TypeSig left, TypeSig right) => !(left == right);

    public static implicit operator TypeSig(TypeDesc type) => new(type);

    sealed class TypeAndMods
    {
        public TypeDesc Type = null!;
        public ImmutableArray<TypeModifier> Mods;
    }
}
public record struct TypeModifier(TypeDesc Type, bool IsRequired);