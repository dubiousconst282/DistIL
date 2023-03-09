namespace DistIL.Passes.Vectorization;

internal struct VectorTreeBuilder
{
    public required VectorTreeStamper Stamper;
    public required VectorType VecType;
    public float Cost;

    public VectorNode BuildTree(Value[] lanes, int depth = 1)
    {
        var anchor = lanes[0];
        bool canBePacked = true;

        for (int i = 0; i < lanes.Length && canBePacked; i++) {
            if (i > 0 && !AreIsomorphic(anchor, lanes[i])) {
                canBePacked = false;
            }
            if (lanes[i] is Instruction inst) {
                canBePacked &= Stamper.Contains(inst);
                canBePacked &= inst.NumUses == 1;
            }
        }
        //TODO: handle widening/narrowing for intermediate ops
        canBePacked &= anchor.ResultType == VecType.ElemType;

        if (canBePacked && depth < 32) {
            switch (anchor) {
                case LoadPtrInst: {
                    return BuildLoadNode(lanes);
                }
                case BinaryInst inst when GetBinOp(inst.Op, out var op): {
                    return BuildOpNode(op, lanes, depth);
                }
                case CallInst call when GetMathOp(call.Method, out var op): {
                    return BuildOpNode(op, lanes, depth);
                }
                default: break;
            }
        }
        if (lanes.All(e => e.Equals(anchor))) {
            Cost += anchor is Const ? 0 : 1;
            return new ScalarNode() { Type = VecType, Arg = anchor };
        }
        Cost += (VecType.Count - 1) * 2; //insert at 0th index is cheap, others not so much.
        return new PackNode() { Type = VecType, Args = lanes };
    }

    private VectorNode BuildLoadNode(Value[] lanes)
    {
        var addrs = new AddrInfo[lanes.Length];
        bool allSameIndex = true, allConsecutive = true;
        int minDispIdx = 0, maxDispIdx = 0;

        for (int i = 0; i < lanes.Length; i++) {
            var load = (LoadPtrInst)lanes[i];
            addrs[i] = AddrInfo.Decompose(load.Address);

            if (i == 0) continue;

            allSameIndex &= addrs[0].SameBase(addrs[i]);
            allConsecutive &= addrs[i].Index == addrs[i - 1].Index + 1;

            if (addrs[i].Index < addrs[minDispIdx].Index) {
                minDispIdx = i;
            }
            if (addrs[i].Index > addrs[maxDispIdx].Index) {
                maxDispIdx = i;
            }
        }
        int maxDist = addrs[maxDispIdx].Index - addrs[minDispIdx].Index;

        //If all loads are within the same vector block, a single load will do.
        if (allSameIndex && (allConsecutive || maxDist + 1 == lanes.Length)) {
            var baseAddr = ((LoadPtrInst)lanes[minDispIdx]).Address;
            var node = Stamper.TieFibers(new LoadNode() { Type = VecType, Address = baseAddr }, lanes);
            Cost -= VecType.Count * 1.25f;

            if (!allConsecutive) {
                int baseDisp = addrs[minDispIdx].Index;
                node = new ShuffleNode() {
                    Type = VecType,
                    Indices = addrs.Select(a => a.Index - baseDisp).ToArray(),
                    Arg = node
                };
                Cost += 0.5f;
            }
            return node;
        }
        //TODO: handle gather
        Cost += lanes.Length * 3;
        return new PackNode() { Type = VecType, Args = lanes };
    }

    private VectorNode BuildOpNode(VectorOp op, Value[] lanes, int depth)
    {
        var firstLane = (Instruction)lanes[0];
        var args = new VectorNode[firstLane.Operands.Length];

        for (int i = 0; i < args.Length; i++) {
            var argLanes = new Value[lanes.Length];

            for (int j = 0; j < lanes.Length; j++) {
                var lane = (Instruction)lanes[j];
                argLanes[j] = lane.Operands[i];
            }
            args[i] = BuildTree(argLanes, depth + 1);
        }
        Cost -= VecType.Count * 0.75f;
        return Stamper.TieFibers(new OperationNode() { Type = VecType, Op = op, Args = args }, lanes);
    }

    private static bool AreIsomorphic(Value a, Value b)
    {
        if (a.GetType() != b.GetType() || a.ResultType != b.ResultType) {
            return false;
        }
        return (a, b) switch {
            (BinaryInst ia, BinaryInst ib) => ia.Op == ib.Op,
            (CallInst ia, CallInst ib) => ia.Method == ib.Method,
            (LoadInst ia, LoadInst ib) => ia.LocationType == ib.LocationType,
            _ => false
        };
    }

    private static bool GetBinOp(BinaryOp op, out VectorOp vop)
    {
        vop = op switch {
            BinaryOp.Add => VectorOp.Add,
            BinaryOp.Sub => VectorOp.Sub,
            BinaryOp.Mul => VectorOp.Mul,
            //There's no HW support for integer vector division (x64/ARM),
            //so we might as well not even bother here.

            BinaryOp.And => VectorOp.And,
            BinaryOp.Or => VectorOp.Or,
            BinaryOp.Xor => VectorOp.Xor,
            BinaryOp.Shl => VectorOp.Shl,
            BinaryOp.Shra => VectorOp.Shra,
            BinaryOp.Shrl => VectorOp.Shrl,

            BinaryOp.FAdd => VectorOp.Add,
            BinaryOp.FSub => VectorOp.Sub,
            BinaryOp.FMul => VectorOp.Mul,
            BinaryOp.FDiv => VectorOp.Div,

            _ => (VectorOp)(-1)
        };
        return vop >= 0;
    }

    private static bool GetMathOp(MethodDesc method, out VectorOp vop)
    {
        var declType = method.DeclaringType;
        if (!declType.IsCorelibType() || declType.Name is not "Math" or "MathF") {
            vop = 0;
            return false;
        }
        vop = method.Name switch {
            //TODO: Math.Min/Max() and vector instrs are not equivalent for floats (NaNs and fp stuff)
            "Abs"       => VectorOp.Abs,
            "Min"       => VectorOp.Min,
            "Max"       => VectorOp.Max,
            "Floor"     => VectorOp.Floor,
            "Ceiling"   => VectorOp.Ceil,
            //"Round" when method.ParamSig.Count == 1
            //            => VectorOp.Round,
            "Sqrt"      => VectorOp.Sqrt,
            //"FusedMultiplyAdd" => VectorOp.Fmadd,

            _ => (VectorOp)(-1)
        };
        return vop >= 0;
    }
}