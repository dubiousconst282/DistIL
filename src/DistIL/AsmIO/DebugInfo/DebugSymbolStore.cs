namespace DistIL.AsmIO;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

// https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md
public class DebugSymbolStore
{
    public ModuleDef Module { get; }

    readonly MetadataReader _reader;
    readonly Dictionary<MethodDef, MethodDebugSymbols> _methodSymbols = new();
    
    public IReadOnlyList<SourceDocument> Documents { get; }

    public DebugSymbolStore(ModuleDef module, MetadataReader reader)
    {
        Module = module;
        _reader = reader;
        var docs = new List<SourceDocument>();

        foreach (var handle in reader.Documents) {
            var doc = reader.GetDocument(handle);

            docs.Add(new SourceDocument() {
                Name = reader.GetString(doc.Name),
                Hash = reader.GetBlobBytes(doc.Hash),
                Language = reader.GetGuid(doc.Language),
                HashAlgorithm = reader.GetGuid(doc.HashAlgorithm)
            });
        }
        Documents = docs;
    }

    public MethodDebugSymbols? GetMethodSymbols(MethodDef method)
    {
        Ensure.That(method.Module == Module);

        if (_methodSymbols.TryGetValue(method, out var symbols)) {
            return symbols;
        }

        var info = _reader.GetMethodDebugInformation(method._handle);
        if (info.Document.IsNil) return null;

        var seqPoints = new List<SequencePoint>();

        foreach (var sp in info.GetSequencePoints()) {
            seqPoints.Add(new SequencePoint() {
                Document = GetDocument(sp.Document),
                Offset = sp.Offset,
                StartLine = sp.StartLine,
                EndLine = sp.EndLine,
                StartColumn = (ushort)sp.StartColumn,
                EndColumn = (ushort)sp.EndColumn,
            });
        }

        symbols = new MethodDebugSymbols() {
            Document = GetDocument(info.Document),
            SequencePoints = seqPoints,
            StateMachineKickoffMethod =
                method.Name == "MoveNext" && info.GetStateMachineKickoffMethod() is { IsNil: false } smkHandle
                    ? Module._loader!.GetMethod(smkHandle) : null
        };
        _methodSymbols[method] = symbols;

        return symbols;
    }
    public void SetMethodSymbols(MethodDef method, MethodDebugSymbols symbols)
    {
        _methodSymbols[method] = symbols;
    }

    private SourceDocument GetDocument(DocumentHandle handle)
    {
        return Documents[MetadataTokens.GetRowNumber(handle)];
    }
}
public class MethodDebugSymbols
{
    public required SourceDocument Document { get; init; }
    public required List<SequencePoint> SequencePoints { get; init; }
    public MethodDef? StateMachineKickoffMethod { get; init; }

    /// <summary> Finds the index of a sequence point whose offset is not after <paramref name="offset"/>. </summary>
    public int IndexOfSequencePoint(int offset)
    {
        var points = SequencePoints.AsSpan();
        int start = 0, end = points.Length;

        while (start < end) {
            int mid = (start + end) / 2;

            if (points[mid].Offset < offset) {
                start = mid + 1;
            } else {
                end = mid;
            }
        }
        return start;
    }
}

public class SourceDocument
{
    public string Name { get; init; } = "";
    public byte[] Hash { get; init; } = [];

    public Guid Language { get; init; }
    public Guid HashAlgorithm { get; init; }

    public static readonly Guid
        CSharp = Guid.Parse("3f5162f8-07c6-11d3-9053-00c04fa302a1"),
        VisualBasic = Guid.Parse("3a12d0b8-c26c-11d0-b442-00a0244a1dd2"),
        FSharp = Guid.Parse("ab4f38c9-b6e6-43ba-be3b-58080b2ccce3"),

        SHA1 = Guid.Parse("ff1816ec-aa5e-4d10-87f7-6f4963833460"),
        SHA256 = Guid.Parse("8829d00f-11b8-4213-878b-770e8597ac16");
}
public readonly struct SequencePoint
{
    public SourceDocument? Document { get; init; }
    public int Offset { get; init; }
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public ushort StartColumn { get; init; }
    public ushort EndColumn { get; init; }

    public bool IsHidden => StartLine == 0xfeefee;

    public override string ToString()
        => IsHidden ? "(hidden)" : 
           $"IL_{Offset:X4}, {StartLine}:{StartColumn}-{EndLine}:{EndColumn} in '{Document?.Name.Split(['\\', '/']).Last()}'";
}