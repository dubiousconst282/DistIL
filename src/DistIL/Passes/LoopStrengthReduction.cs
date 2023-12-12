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

            // Don't reduce loops with more than one body block, because the 
            // only benefit it has is in enabling vectorization.
            if (loop.NumBlocks <= 2 && ShapedLoopInfo.Parse(loop) is CountingLoopInfo countingLoop) {
                numChanges += ReduceLoop(countingLoop);
            }
        }
        return numChanges > 0 ? MethodInvalidations.DataFlow : 0;
    }

    // TODO: maybe move this out to a separate pass
    
    // Dumb duplicate IV elimination, if that's a thing.
    private int RemoveDuplicatedCounters(LoopInfo loop, DominatorTree domTree)
    {
        var pred = loop.GetPredecessor();
        var latch = loop.GetLatch();

        // Check for canonical loop
        if (pred == null || latch == null) return 0;

        var phis = new List<(PhiInst Phi, Value StartVal, BinaryInst UpdatedVal)>();

        foreach (var phi in loop.Header.Phis()) {
            var startVal = phi.GetValue(pred);
            var updatedVal = phi.GetValue(latch);

            if (phi.NumArgs == 2 && updatedVal is BinaryInst { Op: BinaryOp.Add } updatedValI && updatedValI.Left == phi) {
                phis.Add((phi, startVal, updatedValI));
            }
        }

        // The loops below are quadratic, bail out if there are too many phis.
        if (phis.Count < 2 || phis.Count > 12) return 0;
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

    private int ReduceLoop(CountingLoopInfo loop)
    {
        // Strength-reducing array indexes in backward loops is not trivial, as the GC
        // doesn't update refs pointing outside an object when compacting the heap.
        // Details: https://github.com/dotnet/runtime/pull/75857#discussion_r974661744
        if (!loop.IsCanonical) return 0;

        var indexedAccs = new Dictionary<Value, (List<Instruction> Addrs, bool BoundsLoop, bool IsSpan)>();

        foreach (var user in loop.Counter.Users()) {
            if (user == loop.UpdatedCounter || user == loop.ExitCondition || !loop.Contains(user.Block)) continue;

            switch (user) {
                case ArrayAddrInst addr 
                when !addr.IsCasting && IsBoundedByArrayLen(addr, loop.ExitCondition) is bool isBounded && (isBounded || addr.InBounds): {
                    ref var info = ref indexedAccs.GetOrAddRef(addr.Array);
                    info.Addrs ??= new();

                    info.Addrs.Add(addr);
                    info.BoundsLoop |= isBounded;
                    break;
                }
                case CallInst { Method.Name: "get_Item" } call when IsBoundedBySpanLen(call, loop.ExitCondition): {
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
            if (isSpan && !IsSpanLive(source, loop.Loop)) continue;
            if (!loop.IsInvariant(source)) continue;
            // Preheader:
            //  ...
            //  T& basePtr = call MemoryMarshal::GetArrayDataReference<T>(T[]: array)
            //  T& endPtr = add basePtr, (mul (arrlen array), sizeof(T))) // if exit cond can be replaced
            //  T& startPtr = add basePtr, (mul iv.Offset, sizeof(T))
            //  goto Header
            // Header:
            //  T& currPtr = phi [Pred: startPtr], [Latch: {currPtr + iv.Scale}]
            bool mayReplaceCond = loop.ExitCondition.Block != null && boundsLoop;
            bool shouldCreateIV = mayReplaceCond && indexedAccs.Count == 1 && addrs.Count == 1 && loop.Counter.NumUses == 3;

            var builder = new IRBuilder(loop.PreHeader);
            var (startPtr, count) = CreateGetDataPtrRange(builder, source, getCount: shouldCreateIV);

            if (shouldCreateIV) {
                var currPtr = loop.Header.InsertPhi(startPtr.ResultType).SetName("lsr_ptr");

                // Replace loop exit condition with `icmp.ult currPtr, endPtr` if not already.
                if (mayReplaceCond) {
                    var op = loop.ExitCondition.Op.GetUnsigned();
                    var endPtr = builder.CreatePtrOffset(startPtr, count);
                    loop.ExitCondition.ReplaceWith(new CompareInst(op, currPtr, endPtr), insertIfInst: true);

                    // Delete old bound access
                    var oldBound = (Instruction)loop.ExitCondition.Right;
                    if (oldBound.NumUses == 0) {
                        oldBound.Remove();
                    }

                    if (oldBound is ConvertInst { Value: IntrinsicInst { NumUses: 0 } oldLen }) {
                        oldLen.Remove();
                    }
                }
                builder.SetPosition(loop.Latch);
                currPtr.AddArg((loop.PreHeader, startPtr), (loop.Latch, builder.CreatePtrIncrement(currPtr)));

                foreach (var addr in addrs) {
                    Debug.Assert(addr is ArrayAddrInst or CallInst && addr.Operands[1] == loop.Counter);

                    addr.ReplaceWith(currPtr);
                }
            } else {
                // Inline index calls with LEAs
                foreach (var addr in addrs) {
                    Debug.Assert(addr is ArrayAddrInst or CallInst);

                    builder.SetPosition(addr, InsertionDir.Before);
                    addr.ReplaceWith(builder.CreatePtrOffset(startPtr, addr.Operands[1]));
                }

                // Hoist array/span length access
                if (loop.ExitCondition.Right is Instruction oldBound && !loop.IsInvariant(oldBound)) {
                    if (oldBound is ConvertInst { Value: IntrinsicInst oldLen }) {
                        // conv(arrlen)
                        if (oldBound.Prev == oldLen) {
                            oldBound.MoveBefore(loop.PreHeader.Last);
                            oldLen.MoveBefore(oldBound);
                        }
                    } else {
                        oldBound.MoveBefore(loop.PreHeader.Last);
                    }
                }
            }
        }

        if (loop.Counter.NumUses == 1 && loop.UpdatedCounter.NumUses == 1) {
            loop.UpdatedCounter.Remove();
            loop.Counter.Remove();
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