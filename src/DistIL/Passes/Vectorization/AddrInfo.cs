namespace DistIL.Passes.Vectorization;

/// <summary> Represents a decomposed addressing expression in the form: <c>BasePtr + (Index + Displacement) * Scale</c>. </summary>
internal struct AddrInfo
{
    public Value BasePtr;
    public Value? Index;
    public int Scale;
    public int Displacement;

    public bool SameIndex(in AddrInfo other)
        => other.BasePtr == BasePtr && other.Index == Index && other.Scale == Scale;

    public bool IsNextAdjacent(in AddrInfo prev)
        => SameIndex(prev) && Displacement == prev.Displacement + 1;

    public static AddrInfo Decompose(Value addr)
    {
        var dec = new AddrInfo() { BasePtr = addr, Scale = 1 };

        if (addr is BinaryInst { Op: BinaryOp.Add } offset) {
            //addr = add base, disp
            if (offset.Right is ConstInt disp1) {
                dec.BasePtr = offset.Left;
                dec.Displacement = checked((int)disp1.Value);
            }
            //addr = add base, (mul index, scale)
            else if (offset.Right is BinaryInst { Op: BinaryOp.Mul, Left: var index, Right: ConstInt scale }) {
                dec.BasePtr = offset.Left;
                dec.Scale = checked((int)scale.Value);

                //index = conv.i actualIndex
                if (index is ConvertInst { ResultType.Kind: TypeKind.IntPtr } conv) {
                    index = conv.Value;
                }
                //index = add offset, disp
                if (index is BinaryInst { Op: BinaryOp.Add, Left: var index2, Right: ConstInt disp2 }) {
                    index = index2;
                    dec.Displacement = checked((int)disp2.Value);
                }
                //Fold constant index+disp
                //  &addr[(2 + 1) * 4]  ->  &addr[(0 + 3) * 4]
                if (index is ConstInt idx) {
                    checked { dec.Displacement += (int)idx.Value; }
                } else {
                    dec.Index = index;
                }
            }
        }

        //Normalize scale assuming known ptr type
        //  &addr[(0 + 4) * 1]  ->  &addr[(0 + 1) * 4]
        if (dec.Scale == 1 &&
            dec.BasePtr.ResultType is PointerType ptr &&
            ptr.ElemType.Kind.Size() is int elemSize and > 0
        ) {
            dec.Scale = elemSize;
            dec.Displacement /= elemSize;
        }
        return dec;
    }

    public override string ToString() 
        => $"&{BasePtr}[({Index?.ToString() ?? "0"} + {Displacement}) * {Scale}]";
}