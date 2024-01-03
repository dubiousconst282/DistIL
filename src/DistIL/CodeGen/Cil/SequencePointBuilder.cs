namespace DistIL.CodeGen.Cil;

public class SequencePointBuilder(MethodDef method)
{
    readonly List<SequencePoint> _points = new();
    readonly SourceDocument _parentDoc = method.GetDebugSymbols()?.Document ?? s_EmptyDoc;

    (MethodDef? M, int I) _lastPoint;
    bool _spansMultipleDocs = false;
    int _lastCilIndex = -1;

    static readonly SourceDocument s_EmptyDoc = new();

    public void Add(SourceLocation loc, int cilIndex)
    {
        if (cilIndex == _lastCilIndex) return;

        Debug.Assert(_lastCilIndex < cilIndex, "Sequence points must be added in ascending order");
        _lastCilIndex = cilIndex;

        var currMethod = loc.GetMethod(method.Module.Resolver);
        var currSymbols = currMethod?.GetDebugSymbols();

        if (currSymbols?.SequencePoints.Count > 0) {
            // Remap a sequence point at the current location and add it at `cilIndex`
            int pointIdx = currSymbols.IndexOfSequencePoint(loc.Offset);

            if (pointIdx >= 0 && _lastPoint != (currMethod, pointIdx)) {
                var point = currSymbols.SequencePoints[pointIdx];
                _points.Add(point with { Offset = cilIndex });

                _spansMultipleDocs |= _lastPoint.M != currMethod && _lastPoint.M != null;
                _lastPoint = (currMethod, pointIdx);
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
        method.Module.GetDebugSymbols()?.SetMethodSymbols(method, symbols);
    }
}