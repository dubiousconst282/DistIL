namespace DistIL.AsmIO;

using System.Reflection.Metadata;

public class CustomAttrib
{
    public MethodDesc Constructor { get; init; } = null!;
    public ImmutableArray<CustomAttribArg> FixedArgs { get; init; }
    public ImmutableArray<CustomAttribArg> NamedArgs { get; init; }
}
public struct CustomAttribArg
{
    public TypeDesc Type { get; init; }
    public object? Value { get; init; }
    public string? Name { get; init; }
    public CustomAttribArgKind Kind { get; init; }
}
public enum CustomAttribArgKind
{
    Fixed,
    Field = CustomAttributeNamedArgumentKind.Field,
    Property = CustomAttributeNamedArgumentKind.Property,
}