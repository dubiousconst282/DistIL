namespace DistIL.AsmIO;

public class MethodDebugSymbols
{
    public required DebugSourceDocument? Document { get; init; }
    public required List<SequencePoint> SequencePoints { get; init; }
    public MethodDef? StateMachineKickoffMethod { get; init; }

    /// <summary> Finds the index of a sequence point at or after <paramref name="offset"/>. </summary>
    public int IndexOfSequencePoint(int offset)
    {
        var points = SequencePoints.AsSpan();
        int start = 0, end = points.Length;

        while (start < end) {
            int mid = (start + end) / 2;

            if (points[mid].Offset > offset) {
                end = mid;
            } else {
                start = mid + 1;
            }
        }
        return end - 1;
    }
}

public record struct SequencePoint
{
    public DebugSourceDocument Document { get; set; }
    public int Offset { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public ushort StartColumn { get; set; }
    public ushort EndColumn { get; set; }

    public bool IsHidden => StartLine == 0xfeefee;

    public static SequencePoint CreateHidden(DebugSourceDocument doc, int offset)
        => new() { Document = doc, StartLine = 0xfeefee, EndLine = 0xfeefee, Offset = offset };

    public static SequencePoint Create(DebugSourceLocation loc, int offset)
        => new() {
            Document = loc.Document,
            Offset = offset,
            StartLine = loc.StartLine,
            EndLine = loc.EndLine,
            StartColumn = loc.StartColumn,
            EndColumn = loc.EndColumn,
        };

        
    public readonly bool IsSameSourceRange(SequencePoint other)
    {
        return Document == other.Document && 
               StartLine == other.StartLine && EndLine == other.EndLine &&
               StartColumn == other.StartColumn && EndColumn == other.EndColumn;
    }

    public override string ToString()
        => $"IL_{Offset:X4}, " + (IsHidden ? "hidden" : $"{StartLine}:{StartColumn}-{EndLine}:{EndColumn} at '{Document}'");
}