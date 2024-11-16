namespace DistIL.AsmIO;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

partial class DebugSymbolStore
{
    internal virtual void Write(PdbBuilder builder)
    {
        var tb = builder.TableBuilder;
        var emptySymbols = new MethodDebugSymbols() { Document = null, SequencePoints = [] };

        // Create method symbols
        foreach (var method in Module.MethodDefs()) {
            // TODO: avoid wasteful materialization of MethodDebugSymbols when possible
            // if (!_methodSymbols.TryGetValue(method, out var symbols)) { }
            var symbols = method.GetDebugSymbols() ?? emptySymbols;

            var handle = tb.AddMethodDebugInformation(
                builder.GetHandle(symbols.Document),
                EncodeSequencePoints(builder, symbols)
            );
            Debug.Assert(MetadataTokens.GetRowNumber(handle) == MetadataTokens.GetRowNumber(builder.GetHandle(method)));

            if (symbols.StateMachineKickoffMethod != null) {
                tb.AddStateMachineMethod(
                    builder.GetHandle(method),
                    builder.GetHandle(symbols.StateMachineKickoffMethod)
                );
            }
        }
    }


    private BlobHandle EncodeSequencePoints(PdbBuilder builder, MethodDebugSymbols symbols)
    {
        var points = symbols.SequencePoints.AsSpan();
        if (points.Length == 0) return default;

        var blob = new BlobBuilder();

        // Header
        blob.WriteCompressedInteger(0); // Local Sig RID
        if (symbols.Document == null) {
            int docRid = MetadataTokens.GetRowNumber(builder.GetHandle(points[0].Document));
            blob.WriteCompressedInteger(docRid);
        }

        int prevNonHiddenIdx = -1;

        for (int i = 0; i < points.Length; i++) {
            var pt = points[i];

            if (i > 0 && points[i - 1].Document != pt.Document) {
                blob.WriteCompressedInteger(0);

                int docRid = MetadataTokens.GetRowNumber(builder.GetHandle(pt.Document));
                blob.WriteCompressedInteger(docRid);
            }

            int prevOffset = i > 0 ? points[i - 1].Offset : 0;
            Ensure.That(i == 0 || pt.Offset != prevOffset);
            blob.WriteCompressedInteger(pt.Offset - prevOffset);

            if (pt.IsHidden) {
                blob.WriteCompressedInteger(0);
                blob.WriteCompressedInteger(0);
                continue;
            }

            int deltaLines = pt.EndLine - pt.StartLine;
            int deltaColumns = pt.EndColumn - pt.StartColumn;

            blob.WriteCompressedInteger(deltaLines);

            if (deltaLines == 0) {
                blob.WriteCompressedInteger(deltaColumns);
            } else {
                blob.WriteCompressedSignedInteger(deltaColumns);
            }

            if (prevNonHiddenIdx < 0) {
                blob.WriteCompressedInteger(pt.StartLine);
                blob.WriteCompressedInteger(pt.StartColumn);
            } else {
                blob.WriteCompressedSignedInteger(pt.StartLine - points[prevNonHiddenIdx].StartLine);
                blob.WriteCompressedSignedInteger(pt.StartColumn - points[prevNonHiddenIdx].StartColumn);
            }
            prevNonHiddenIdx = i;
        }

        return builder.TableBuilder.GetOrAddBlob(blob);
    }
}

internal class PdbBuilder(ModuleWriter modWriter)
{
    public readonly MetadataBuilder TableBuilder = new();
    private readonly Dictionary<DebugSourceDocument, DocumentHandle> _documents = new();
    public readonly ModuleWriter ModWriter = modWriter;

    public DocumentHandle GetHandle(DebugSourceDocument? doc)
    {
        if (doc == null) return default;

        ref var handle = ref _documents.GetOrAddRef(doc, out bool exists);
        if (exists) return handle;

        return handle = TableBuilder.AddDocument(
            TableBuilder.GetOrAddDocumentName(doc.Name),
            TableBuilder.GetOrAddGuid(doc.HashAlgorithm),
            TableBuilder.GetOrAddBlob(doc.Hash),
            TableBuilder.GetOrAddGuid(doc.Language)
        );
    }
    public MethodDefinitionHandle GetHandle(MethodDef method)
    {
        return (MethodDefinitionHandle)ModWriter.GetHandle(method);
    }
}
partial class PortablePdbSymbolStore
{
    static readonly Guid[] s_PassthroughCdiKinds = [
        Guid.Parse("0E8A571B-6926-466E-B4AD-8AB04611F5FE"), // Embedded Sources
        Guid.Parse("CC110556-A091-4D38-9FEC-25AB9A351A6A"), // Source Link
        Guid.Parse("7E4D4708-096E-4C5C-AEDA-CB10BA6A740D"), // Compilation Metadata References
        Guid.Parse("B5FEEC05-8CD0-4A83-96DA-466284BB4BD8"), // Compilation Options
    ];

    internal override void Write(PdbBuilder writer)
    {
        base.Write(writer);

        var tb = writer.TableBuilder;

        // Copy CDI entries that don't need changes
        foreach (var handle in _reader.CustomDebugInformation) {
            var info = _reader.GetCustomDebugInformation(handle);
            var kind = _reader.GetGuid(info.Kind);

            if (!s_PassthroughCdiKinds.AsSpan().Contains(kind)) continue;

            var parentHandle = info.Parent;

            // Remap handle
            if (parentHandle.Kind == HandleKind.Document) {
                parentHandle = writer.GetHandle(GetDocument((DocumentHandle)parentHandle));
            } else if (parentHandle.Kind != HandleKind.ModuleDefinition) {
                parentHandle = writer.ModWriter.GetHandle(Module._loader!.GetEntity(parentHandle));
            }

            if (parentHandle.IsNil) continue;

            tb.AddCustomDebugInformation(
                parentHandle,
                tb.GetOrAddGuid(kind),
                tb.GetOrAddBlob(_reader.GetBlobBytes(info.Value))
            );
        }
    }
}