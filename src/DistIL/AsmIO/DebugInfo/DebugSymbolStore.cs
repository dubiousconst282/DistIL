namespace DistIL.AsmIO;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

// https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md
public partial class DebugSymbolStore(ModuleDef module)
{
    public ModuleDef Module { get; } = module;

    internal readonly Dictionary<MethodDef, MethodDebugSymbols> _methodSymbols = new();

    public virtual MethodDebugSymbols? GetMethodSymbols(MethodDef method)
    {
        Ensure.That(method.Module == Module);
        return _methodSymbols.GetValueOrDefault(method);
    }
    public void SetMethodSymbols(MethodDef method, MethodDebugSymbols? symbols)
    {
        if (symbols != null) {
            _methodSymbols[method] = symbols;
        } else {
            _methodSymbols.Remove(method);
        }
    }

    /// <summary> Scans for all documents associated with the current debug symbols. </summary>
    public virtual IReadOnlyCollection<DebugSourceDocument> GetDocuments()
    {
        var set = new HashSet<DebugSourceDocument>();

        foreach (var method in _methodSymbols.Values) {
            if (method.Document != null) {
                set.Add(method.Document);
            }

            foreach (var sp in method.SequencePoints) {
                if (sp.Document != null) {
                    set.Add(sp.Document);
                }
            }
        }

        return set;
    }
}

internal partial class PortablePdbSymbolStore : DebugSymbolStore
{
    readonly MetadataReader _reader;
    readonly DebugSourceDocument[] _documents;

    public PortablePdbSymbolStore(ModuleDef module, MetadataReader reader)
        : base(module)
    {
        _reader = reader;

        _documents = new DebugSourceDocument[reader.Documents.Count];
        int idx = 0;

        foreach (var handle in reader.Documents) {
            var doc = reader.GetDocument(handle);

            _documents[idx++] = new DebugSourceDocument() {
                Name = reader.GetString(doc.Name),
                Hash = reader.GetBlobBytes(doc.Hash),
                Language = reader.GetGuid(doc.Language),
                HashAlgorithm = reader.GetGuid(doc.HashAlgorithm)
            };
        }
    }

    public override MethodDebugSymbols? GetMethodSymbols(MethodDef method)
    {
        var symbols = base.GetMethodSymbols(method);
        if (symbols != null || method._handle.IsNil) {
            return symbols;
        }

        var info = _reader.GetMethodDebugInformation(method._handle);
        if (info.Document.IsNil && info.SequencePointsBlob.IsNil) {
            return null;
        }

        var seqPoints = new List<SequencePoint>();

        foreach (var sp in info.GetSequencePoints()) {
            seqPoints.Add(new SequencePoint() {
                Document = GetDocument(sp.Document)!,
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
        SetMethodSymbols(method, symbols);
        return symbols;
    }

    public override IReadOnlyCollection<DebugSourceDocument> GetDocuments()
    {
        var docs = (HashSet<DebugSourceDocument>)base.GetDocuments();

        if (docs.Count == 0) {
            return _documents;
        }
        docs.UnionWith(_documents);
        return docs;
    }

    private DebugSourceDocument? GetDocument(DocumentHandle handle)
    {
        return handle.IsNil ? null : _documents[MetadataTokens.GetRowNumber(handle) - 1];
    }
}
