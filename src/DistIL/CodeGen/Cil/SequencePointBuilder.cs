namespace DistIL.CodeGen.Cil;

public class SequencePointBuilder(MethodDef method)
{
    readonly List<SequencePoint> _points = new();
    readonly DebugSourceDocument _parentDoc = method.GetDebugSymbols()?.Document ?? s_EmptyDoc;

    bool _spansMultipleDocs = false;
    int _lastCilIndex = -1;

    static readonly DebugSourceDocument s_EmptyDoc = new();

    public void Add(DebugSourceLocation? loc, int cilIndex)
    {
        if (cilIndex == _lastCilIndex) return;

        Debug.Assert(_lastCilIndex < cilIndex, "Sequence points must be added in ascending order");
        _lastCilIndex = cilIndex;

        if (loc != null) {
            var sp = SequencePoint.Create(loc, cilIndex);

            if (_points.Count == 0 || !sp.IsSameSourceRange(_points[^1])) {
                _points.Add(sp);
                _spansMultipleDocs |= _points.Count > 0 && _points[^1].Document != loc.Document;
            }
        } else if (_points.Count > 0 && !_points[^1].IsHidden) {
            // If there's no sequence point at the current location in the source,
            // create a hidden one to break the previous span.
            _points.Add(SequencePoint.CreateHidden(_parentDoc, cilIndex));
        }
    }

    public MethodDebugSymbols? BuildSymbols(ReadOnlySpan<ILInstruction> insts)
    {
        if (_points.Count == 0) return null;

        // Remap instruction indices to offsets
        foreach (ref var pt in _points.AsSpan()) {
            pt.Offset = insts[pt.Offset].Offset;
        }

        return new MethodDebugSymbols() {
            Document = _spansMultipleDocs ? null : _parentDoc,
            SequencePoints = _points
        };
    }

    /// <summary> Builds and replaces the debug symbols of the method given in the constructor. </summary>
    public void BuildAndReplace()
    {
        var symbols = BuildSymbols(method.ILBody!.Instructions);
        method.Module.GetDebugSymbols(create: true)?.SetMethodSymbols(method, symbols);
    }
}