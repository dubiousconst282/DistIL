namespace DistIL.Util;

public record struct AbsRange(int Start, int End)
{
    public int Length => End - Start;
    public bool IsEmpty => Start == End;

    public bool Contains(int index) => index >= Start && index < End;

    public override string ToString() => $"{Start}..{End}";

    public static implicit operator Range(AbsRange r) => new(r.Start, r.End);
    public static implicit operator AbsRange(ValueTuple<int, int> r) => new(r.Item1, r.Item2);
}