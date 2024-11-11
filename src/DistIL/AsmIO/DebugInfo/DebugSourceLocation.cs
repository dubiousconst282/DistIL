namespace DistIL.AsmIO;

public class DebugSourceLocation(DebugSourceDocument doc, int startLine, int endLine, int startColumn, int endColumn)
{
    public DebugSourceDocument Document { get; } = doc;
    public int StartLine { get; set; } = startLine;
    public int EndLine { get; set; } = endLine;
    public ushort StartColumn { get; set; } = ushort.CreateSaturating(startColumn);
    public ushort EndColumn { get; set; } = ushort.CreateSaturating(endColumn);

    public DebugSourceLocation(SequencePoint sp)
        : this(sp.Document, sp.StartLine, sp.EndLine, sp.StartColumn, sp.EndColumn) { }

    public override string ToString() => $"{Document}:{StartLine}.{StartColumn}-{EndColumn}";
}