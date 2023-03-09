namespace DistIL.Passes.Vectorization;

/// <summary> Represents a decomposed addressing expression in the form: <c>BasePtr + Index * Scale</c>. </summary>
internal struct AddrInfo
{
    public Value BasePtr;
    public int Index;
    public int Scale;

    public bool SameBase(in AddrInfo other)
        => other.BasePtr == BasePtr && other.Scale == Scale;

    public bool IsNextAdjacent(in AddrInfo prev)
        => SameBase(prev) && Index == prev.Index + 1;

    public static AddrInfo Decompose(Value addr)
    {
        var dec = new AddrInfo() { BasePtr = addr, Scale = 1 };

        //addr = add base, disp
        if (addr is BinaryInst { Op: BinaryOp.Add, Right: ConstInt disp } bin) {
            dec.BasePtr = bin.Left;
            dec.Index = checked((int)disp.Value);
        }
        //addr = lea base + idx * stride
        else if (addr is PtrOffsetInst { Index: ConstInt idx, KnownStride: true } lea) {
            dec.BasePtr = lea.BasePtr;
            dec.Index = checked((int)idx.Value);
            dec.Scale = lea.Stride;
        }

        //Normalize scale assuming known ptr type
        //  &addr[(0 + 4) * 1]  ->  &addr[(0 + 1) * 4]
        if (dec.Scale == 1 &&
            dec.BasePtr.ResultType is PointerType ptr &&
            ptr.ElemType.Kind.Size() is int elemSize and > 0
        ) {
            dec.Scale = elemSize;
            dec.Index /= elemSize;
        }
        return dec;
    }

    public override string ToString() => $"[{Index} * {Scale}] at {BasePtr}";
}