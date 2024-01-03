namespace DistIL.AsmIO;

public class SourceDocument
{
    public string Name { get; init; } = "";
    public byte[] Hash { get; init; } = [];

    public Guid Language { get; init; }
    public Guid HashAlgorithm { get; init; }

    public override string ToString() => Path.GetFileName(Name);
    
    public override bool Equals(object? obj) => obj is SourceDocument other && other.Name == Name;
    public override int GetHashCode() => Name.GetHashCode();

    // Known GUIDs
    public static readonly Guid
        CSharp = Guid.Parse("3f5162f8-07c6-11d3-9053-00c04fa302a1"),
        VisualBasic = Guid.Parse("3a12d0b8-c26c-11d0-b442-00a0244a1dd2"),
        FSharp = Guid.Parse("ab4f38c9-b6e6-43ba-be3b-58080b2ccce3"),

        SHA1 = Guid.Parse("ff1816ec-aa5e-4d10-87f7-6f4963833460"),
        SHA256 = Guid.Parse("8829d00f-11b8-4213-878b-770e8597ac16");
}