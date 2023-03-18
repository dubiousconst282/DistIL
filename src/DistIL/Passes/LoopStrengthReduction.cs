namespace DistIL.Passes;

using DistIL.Analysis;
using DistIL.IR.Intrinsics;
using DistIL.IR.Utils;

/// <summary> Replaces array indexing inside for-loops with pointer-based loops. </summary>
public class LoopStrengthReduction : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var loopAnalysis = ctx.GetAnalysis<LoopAnalysis>(preserve: true);
        int numChanges = 0;

        foreach (var loop in loopAnalysis.Loops) {
            numChanges += ReduceLoop(loop);
        }
        return numChanges > 0 ? MethodInvalidations.DataFlow : 0;
    }

    private int ReduceLoop(LoopInfo loop)
    {
        var preheader = loop.GetPreheader();
        var latch = loop.GetLatch();
        var exitCond = loop.GetExitCondition();

        //Check for canonical loop
        if (preheader == null || latch == null || exitCond == null) return 0;

        if (!(exitCond.Left is PhiInst counter && counter.Block == loop.Header)) return 0;

        //Strength-reducing array indexes in backward loops is not trivial, as the GC
        //doesn't update refs pointing outside an object when compacting the heap.
        //Details: https://github.com/dotnet/runtime/pull/75857#discussion_r974661744
        if (!(
            exitCond.Op is CompareOp.Slt or CompareOp.Ult &&
            counter.GetValue(preheader) is ConstInt { Value: 0 } &&
            counter.GetValue(latch) is BinaryInst { Op: BinaryOp.Add, Right: ConstInt { Value: 1 } } steppedCounter
        )) return 0;

        var indexedArrays = new List<ArrayAddrInst>();

        foreach (var user in counter.Users()) {
            if (user == steppedCounter || user == exitCond || !loop.Contains(user.Block)) continue;

            switch (user) {
                case ArrayAddrInst addr when IsArrayInBounds(addr, exitCond): {
                    indexedArrays.Add(addr);
                    break;
                }
                default: {
                    return 0; //Reduce everything or nothing
                }
            }
        }

        foreach (var addr in indexedArrays) {
            //Preheader:
            //  ...
            //  T& basePtr = call MemoryMarshal::GetArrayDataReference<T>(T[]: array)
            //  T& endPtr = add basePtr, (mul (arrlen array), sizeof(T))) //if exit cond can be replaced
            //  T& startPtr = add basePtr, (mul iv.Offset, sizeof(T))
            //  goto Header
            //Header:
            //  T& currPtr = phi [Pred: startPtr], [Latch: {currPtr + iv.Scale}]
            var builder = new IRBuilder(preheader);

            bool shouldReplaceCond = exitCond.Block != null && IsBoundedByArrayLen(addr, exitCond);
            var (startPtr, count) = CreateGetDataPtrRange(builder, addr.Array, getCount: shouldReplaceCond);

            var currPtr = loop.Header.InsertPhi(startPtr.ResultType).SetName("lsr_ptr");

            //Replace loop exit condition with `icmp.ult currPtr, endPtr` if not already.
            if (shouldReplaceCond) {
                var op = exitCond.Op.GetUnsigned();
                var endPtr = builder.CreatePtrOffset(startPtr, count);
                exitCond.ReplaceWith(new CompareInst(op, currPtr, endPtr), insertIfInst: true);
            }
            builder.SetPosition(latch);
            currPtr.AddArg((preheader, startPtr), (latch, builder.CreatePtrIncrement(currPtr)));

            addr.ReplaceWith(currPtr);
        }
        return indexedArrays.Count;
    }

    //TODO: Get rid of this once have range-analysis and InBounds metadata
    private static bool IsArrayInBounds(ArrayAddrInst addr, CompareInst exitCond)
    {
        return !addr.IsCasting && (addr.InBounds || IsBoundedByArrayLen(addr, exitCond));
    }
    private static bool IsBoundedByArrayLen(ArrayAddrInst addr, CompareInst exitCond)
    {
        return exitCond.Op is CompareOp.Slt &&
               exitCond.Left == addr.Index &&
               exitCond.Right is ConvertInst { Value: IntrinsicInst bound, ResultType.Kind: TypeKind.Int32 } &&
               bound.Intrinsic == CilIntrinsic.ArrayLen && bound.Args[0] == addr.Array;
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
                .GetSpec(new GenericContext(methodArgs: new[] { elemType }));

            return builder.CreateCall(m_GetArrayDataRef, source);
        }
    }
}