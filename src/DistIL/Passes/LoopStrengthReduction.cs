namespace DistIL.Passes;

using DistIL.Analysis;
using DistIL.IR.Utils;

/// <summary> Replaces array indexing inside for-loops with pointer-based loops. </summary>
public class LoopStrengthReduction : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var loopAnalysis = ctx.GetAnalysis<LoopAnalysis>(preserve: true);
        var domTree = ctx.GetAnalysis<DominatorTree>(preserve: true);
        int numChanges = 0;

        foreach (var loop in loopAnalysis.Loops) {
            numChanges += RemoveDuplicatedCounters(loop, domTree);
            numChanges += ReduceLoop(loop);
        }
        return numChanges > 0 ? MethodInvalidations.DataFlow : 0;
    }

    // TODO: maybe move this out to a separate pass
    
    // Dumb duplicate IV elimination, if that's a thing.
    private int RemoveDuplicatedCounters(LoopInfo loop, DominatorTree domTree)
    {
        var preheader = loop.GetPreheader();
        var latch = loop.GetLatch();

        // Check for canonical loop
        if (preheader == null || latch == null) return 0;

        var phis = new List<(PhiInst Phi, Value StartVal, BinaryInst UpdatedVal)>();

        foreach (var phi in loop.Header.Phis()) {
            var startVal = phi.GetValue(preheader);
            var updatedVal = phi.GetValue(latch);

            if (phi.NumArgs == 2 && updatedVal is BinaryInst { Op: BinaryOp.Add } updatedValI && updatedValI.Left == phi) {
                phis.Add((phi, startVal, updatedValI));
            }
        }

        // The loops below are quadratic, bail out if there are too many phis.
        if (phis.Count > 12) return 0;
        int numChanges = 0;

        for (int i = 0; i < phis.Count; i++) {
            for (int j = 0; j < phis.Count; j++) {
                var (a, b) = (phis[i], phis[j]);
                if (i == j || a.Phi.ResultType != b.Phi.ResultType) continue;
                if (!a.StartVal.Equals(b.StartVal)) continue;
                if (!a.UpdatedVal.Right.Equals(b.UpdatedVal.Right)) continue;

                if (domTree.Dominates(b.UpdatedVal, a.UpdatedVal)) {
                    a.Phi.ReplaceWith(b.Phi);
                    phis.RemoveAt(i--);
                    numChanges++;
                    break;
                }
            }
        }

        return numChanges;
    }

    private int ReduceLoop(LoopInfo loop)
    {
        var preheader = loop.GetPreheader();
        var latch = loop.GetLatch();
        var exitCond = loop.GetExitCondition();

        // Check for canonical loop
        if (preheader == null || latch == null || exitCond == null) return 0;

        if (!(exitCond.Left is PhiInst counter && counter.Block == loop.Header)) return 0;

        // The only benefit from LSR is enabling vectorization, which only works
        // with loops that have a single body block. Bail if that's not the case.
        if (loop.NumBlocks > 2) return 0;

        // Strength-reducing array indexes in backward loops is not trivial, as the GC
        // doesn't update refs pointing outside an object when compacting the heap.
        // Details: https://github.com/dotnet/runtime/pull/75857#discussion_r974661744
        if (!(
            exitCond.Op is CompareOp.Slt or CompareOp.Ult &&
            counter.GetValue(preheader) is ConstInt { Value: 0 } &&
            counter.GetValue(latch) is BinaryInst { Op: BinaryOp.Add, Right: ConstInt { Value: 1 } } steppedCounter
        )) return 0;

        var indexedAccs = new Dictionary<Value, (List<Instruction> Addrs, bool BoundsLoop, bool IsSpan)>();

        foreach (var user in counter.Users()) {
            if (user == steppedCounter || user == exitCond || !loop.Contains(user.Block)) continue;

            switch (user) {
                case ArrayAddrInst addr 
                when !addr.IsCasting && IsBoundedByArrayLen(addr, exitCond) is bool isBounded && (isBounded || addr.InBounds): {
                    ref var info = ref indexedAccs.GetOrAddRef(addr.Array);
                    info.Addrs ??= new();

                    info.Addrs.Add(addr);
                    info.BoundsLoop |= isBounded;
                    break;
                }
                case CallInst { Method.Name: "get_Item" } call when IsBoundedBySpanLen(call, exitCond): {
                    ref var info = ref indexedAccs.GetOrAddRef(call.Args[0]);
                    info.Addrs ??= new();

                    info.Addrs.Add(call);
                    info.BoundsLoop = true;
                    info.IsSpan = true;
                    break;
                }
            }
        }

        foreach (var (source, (addrs, boundsLoop, isSpan)) in indexedAccs) {
            if (isSpan && !IsSpanLive(source, loop)) continue;
            if (!loop.IsInvariant(source)) continue;
            // Preheader:
            //  ...
            //  T& basePtr = call MemoryMarshal::GetArrayDataReference<T>(T[]: array)
            //  T& endPtr = add basePtr, (mul (arrlen array), sizeof(T))) // if exit cond can be replaced
            //  T& startPtr = add basePtr, (mul iv.Offset, sizeof(T))
            //  goto Header
            // Header:
            //  T& currPtr = phi [Pred: startPtr], [Latch: {currPtr + iv.Scale}]
            bool mayReplaceCond = exitCond.Block != null && boundsLoop;
            bool shouldCreateIV = mayReplaceCond && indexedAccs.Count == 1 && addrs.Count == 1 && counter.NumUses == 3;

            var builder = new IRBuilder(preheader);
            var (startPtr, count) = CreateGetDataPtrRange(builder, source, getCount: shouldCreateIV);

            if (shouldCreateIV) {
                var currPtr = loop.Header.InsertPhi(startPtr.ResultType).SetName("lsr_ptr");

                // Replace loop exit condition with `icmp.ult currPtr, endPtr` if not already.
                if (mayReplaceCond) {
                    var op = exitCond.Op.GetUnsigned();
                    var endPtr = builder.CreatePtrOffset(startPtr, count);
                    exitCond.ReplaceWith(new CompareInst(op, currPtr, endPtr), insertIfInst: true);

                    // Delete old bound access
                    var oldBound = (Instruction)exitCond.Right;
                    if (oldBound.NumUses == 0) {
                        oldBound.Remove();
                    }

                    if (oldBound is ConvertInst { Value: IntrinsicInst { NumUses: 0 } oldLen }) {
                        oldLen.Remove();
                    }
                }
                builder.SetPosition(latch);
                currPtr.AddArg((preheader, startPtr), (latch, builder.CreatePtrIncrement(currPtr)));

                foreach (var addr in addrs) {
                    Debug.Assert(addr is ArrayAddrInst or CallInst && addr.Operands[1] == counter);

                    addr.ReplaceWith(currPtr);
                }
            } else {
                // Replacing complex addressings with leas is still worth, do it
                foreach (var addr in addrs) {
                    Debug.Assert(addr is ArrayAddrInst or CallInst);

                    builder.SetPosition(addr, InsertionDir.Before);
                    addr.ReplaceWith(builder.CreatePtrOffset(startPtr, addr.Operands[1]));
                }

                // Hoist array/span length access
                if (exitCond.Right is Instruction oldBound && !loop.IsInvariant(oldBound)) {
                    if (oldBound is ConvertInst { Value: IntrinsicInst oldLen }) {
                        // conv(arrlen)
                        if (oldBound.Prev == oldLen) {
                            oldBound.MoveBefore(preheader.Last);
                            oldLen.MoveBefore(oldBound);
                        }
                    } else {
                        oldBound.MoveBefore(preheader.Last);
                    }
                }
            }
        }

        if (counter.NumUses == 1) {
            steppedCounter.Remove();
            counter.Remove();
        }
        return indexedAccs.Count;
    }

    // TODO: Get rid of this once have range-analysis and InBounds metadata
    private static bool IsBoundedByArrayLen(ArrayAddrInst addr, CompareInst exitCond)
    {
        // exitCond is cmp.slt ($index, $array.Length)
        return exitCond.Op is CompareOp.Slt &&
               exitCond.Left == addr.Index &&
               exitCond.Right is ConvertInst { Value: CilIntrinsic.ArrayLen bound, ResultType.Kind: TypeKind.Int32 } &&
               bound.Args[0] == addr.Array;
    }
    private static bool IsBoundedBySpanLen(CallInst getItemCall, CompareInst exitCond)
    {
        // exitCond is cmp.slt ($index, $span.Length)
        return exitCond.Op is CompareOp.Slt &&
               exitCond.Left == getItemCall.Args[1] &&
               exitCond.Right is CallInst { Method.Name: "get_Length" } bound &&
               bound.Args[0] == getItemCall.Args[0] && IsSpanMethod(bound.Method);
    }
    // Checks if the span is used by any instruction other than a instance call, indicating that it may have been reassigned.
    private static bool IsSpanLive(Value source, LoopInfo loop)
    {
        return source is TrackedValue def &&
               def.Users().All(u => !loop.Contains(u.Block) || (u is CallInst call && IsSpanMethod(call.Method)));
    }
    private static bool IsSpanMethod(MethodDesc method)
    {
        return method.DeclaringType.IsCorelibType() && 
               method.DeclaringType.Name is "Span`1" or "ReadOnlySpan`1";
    }
    
    /// <summary>
    /// Builds a sequence accessing the underlying ref and count from <paramref name="source"/>, assuming its type is one of:
    /// <see cref="ArrayType"/>, <see cref="string"/>, or <see cref="List{T}"/>.
    /// </summary>
    /// <remarks>
    /// Users should ensure exact types before calling this method, as it matches by name and thus could incorrectly 
    /// match some unrelated type named "List`1".
    /// 
    /// The returned count type may be either int or nint.
    /// </remarks>
    public static (Value BasePtr, Value Count) CreateGetDataPtrRange(IRBuilder builder, Value source, bool getCount = true)
    {
        var (basePtr, count) = source.ResultType switch {
            ArrayType => (
                CreateGetArrayDataRef(builder, source) as Value,
                getCount ? builder.CreateArrayLen(source) : null as Value
            ),
            TypeDesc { Kind: TypeKind.String } => (
                builder.CreateCallVirt("GetPinnableReference", source),
                getCount ? builder.CreateCallVirt("get_Length", source) : null
            ),
            TypeSpec { Name: "List`1" } => (
                CreateGetArrayDataRef(builder, builder.CreateFieldLoad("_items", source)),
                getCount ? builder.CreateFieldLoad("_size", source) : null
            ),
            // TODO: check if it's ok to access span fields directly
            PointerType { ElemType: { Name: "Span`1" or "ReadOnlySpan`1"} } => (
                builder.CreateFieldLoad("_reference", source),
                getCount ? builder.CreateFieldLoad("_length", source) : null
            )
        };
        return (basePtr, count!);

        static CallInst CreateGetArrayDataRef(IRBuilder builder, Value source)
        {
            var elemType = ((ArrayType)source.ResultType).ElemType;
            var T0 = GenericParamType.GetUnboundM(0);

            var m_GetArrayDataRef = builder.Resolver
                .Import(typeof(System.Runtime.InteropServices.MemoryMarshal))
                .FindMethod("GetArrayDataReference", new MethodSig(T0.CreateByref(), new TypeSig[] { T0.CreateArray() }, numGenPars: 1))
                .GetSpec([elemType]);

            return builder.CreateCall(m_GetArrayDataRef, source);
        }
    }
}