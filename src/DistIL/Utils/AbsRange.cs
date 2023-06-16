namespace DistIL.Util;

/// <summary> Represents an absolute index range, [start, end) </summary>
/// <param name="Start"> Start index (inclusive). </param>
/// <param name="End"> End index (exclusive). </param>
public readonly record struct AbsRange(int Start, int End)
{
    public int Length => End - Start;
    public bool IsEmpty => Start == End;

    public static AbsRange FromSlice(int start, int length) => new(start, start + length);

    public bool Contains(int index) => index >= Start && index < End;

    public override string ToString() => $"{Start}..{End}";

    public static implicit operator Range(AbsRange r) => new(r.Start, r.End);
    public static implicit operator AbsRange(ValueTuple<int, int> r) => new(r.Item1, r.Item2);
}