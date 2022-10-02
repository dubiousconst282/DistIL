namespace DistIL.AsmIO;

using System.Reflection.Metadata;

public class CustomAttrib
{
    public required MethodDesc Constructor { get; init; }
    public ImmutableArray<Argument> FixedArgs { get; init; }
    public ImmutableArray<Argument> NamedArgs { get; init; }

    public struct Argument
    {
        public TypeDesc Type { get; init; }
        public object? Value { get; init; }
        public string? Name { get; init; }
        public ArgumentKind Kind { get; init; }
    }
    public enum ArgumentKind : byte
    {
        Fixed,
        Field = CustomAttributeNamedArgumentKind.Field,
        Property = CustomAttributeNamedArgumentKind.Property,
    }
}

/// <summary> Represents a key that links a custom attribute with an module entity. </summary>
internal struct CustomAttribLink : IEquatable<CustomAttribLink>
{
    public ModuleEntity Entity;
    public Type LinkType;
    public int Index;

    public bool Equals(CustomAttribLink other)
        => other.Entity == Entity && other.LinkType == LinkType && other.Index == Index;

    public override int GetHashCode()
        => HashCode.Combine(Entity, LinkType, Index);

    public override bool Equals(object? obj)
        => obj is CustomAttribLink other && Equals(other);

    public enum Type
    {
        Entity,
        MethodParam,
        InterfaceImpl,
        GenericParam,
        GenericConstraint
    }
}