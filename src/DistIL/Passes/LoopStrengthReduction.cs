namespace DistIL.Passes;

using DistIL.Analysis;
using DistIL.IR.Intrinsics;
using DistIL.IR.Utils;

//Key is either an Instruction or a (Value Array, Value Index)
using InductionVarMap = Dictionary<object, LoopStrengthReduction.InductionVar>;

public class LoopStrengthReduction : IMethodPass
{
    bool _removeAllBoundChecks = false;

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
        var prehdr = loop.GetPreheader();
        var latch = loop.GetLatch();

        //Check for canonical loop
        if (prehdr == null || latch == null) return 0;

        var indVars = FindInductionVars(loop, prehdr, latch);
        var cond = GetCanonicalForeachLoopCond(loop);

        int numReduced = 0;

        //Consider candidates and rewrite code
        foreach (var (def, iv) in indVars) {
            switch (def) {
                case BinaryInst { Op: BinaryOp.Add or BinaryOp.Mul } inst: {
                    //If the only user of this inst is a derived IV, don't do anything and let DCE remove it
                    if (inst.NumUses == 1 && indVars.ContainsKey(inst.Users().First())) break;
                    //Only reduce pointer related IVs, to avoid inadvertedly increasing
                    //register pressure and adding loop-caried dependencies.
                    //See https://stackoverflow.com/questions/72306573/why-does-this-code-execute-more-slowly-after-strength-reducing-multiplications-t
                    if (!inst.Users().Any(u => u is PtrAccessInst)) break;
                    if (iv.Offset is not TrackedValue basePtr || basePtr.Users().Count(u => loop.Contains(u.Block)) > 2) break;

                    bool mayReplaceCond = cond.Cmp != null && cond.Cmp.Left == iv.Base && loop.IsInvariant(cond.Cmp.Right);
                    if (!mayReplaceCond) break;

                    //Test should only be replaced if the index has a single use inside the loop
                    //(actually 3: cmp, iv update, maybe a dead conv still using it)
                    bool shouldReplaceCond = 
                        mayReplaceCond && cond.Cmp!.Block != null && 
                        (iv.Base as TrackedValue)?.Users().Count(u => loop.Contains(u.Block)) <= 3;

                    //Header:
                    //  T scaledIV = phi [Preheader: basePtr], [Latch: {scaledIV + iv.Scale}]
                    var currPtr = loop.Header.InsertPhi(inst.ResultType).SetName("lsr_ptr");
                    var nextPtr = new BinaryInst(BinaryOp.Add, currPtr, iv.Scale);
                    latch.InsertAnteLast(nextPtr);
                    currPtr.AddArg((prehdr, basePtr), (latch, nextPtr));
                    inst.ReplaceWith(currPtr);

                    if (shouldReplaceCond) {
                        var builder = new IRBuilder(prehdr);
                        //basePtr + (nint)bound * scale
                        var endPtr = builder.CreateAdd(
                            basePtr, 
                            builder.CreateMul(builder.CreateConvert(cond.Cmp!.Right, PrimType.IntPtr), iv.Scale));

                        var op = cond.Cmp!.Op.GetUnsigned();
                        cond.Cmp.ReplaceWith(new CompareInst(op, currPtr, endPtr), insertIfInst: true);
                    }
                    numReduced++;
                    break;
                }
                case (TrackedValue { ResultType: ArrayType } array, Value index): {
                    //Ensure the index is bounded by the array length.
                    if ((cond.Source != array || cond.Index != index) && !_removeAllBoundChecks) break;

                    //Strength-reducing array indexes in backward loops is not trivial, as the GC does not
                    //update refs pointing before the start of an object when compacting the heap.
                    //Details: https://github.com/dotnet/runtime/pull/75857#discussion_r974661744
                    if (iv.Offset is ConstInt { Value: < 0 } || iv.Scale is ConstInt { Value: < 0 }) break;

                    bool mayReplaceCond = (cond.Source == array || _removeAllBoundChecks) && iv.Scale is ConstInt { Value: 1 };
                    bool shouldReplaceCond = mayReplaceCond && cond.Cmp?.Block != null;

                    //Reduction may only be beneficial if all uses inside the loop can be replaced
                    if (!mayReplaceCond && array.Users().Any(u => u is ArrayAddrInst acc && acc.Index != index && loop.Contains(u.Block))) break;

                    //Preheader:
                    //  ...
                    //  T& basePtr = call MemoryMarshal::GetArrayDataReference<T>(T[]: array)
                    //  T& endPtr = add basePtr, (mul (arrlen array), sizeof(T))) //if exit cond can be replaced
                    //  T& startPtr = add basePtr, (mul iv.Offset, sizeof(T))
                    //  goto Header
                    var builder = new IRBuilder(prehdr);
                    var dataRange = CreateGetDataPtrRange(builder, array, getCount: shouldReplaceCond);
                    var startPtr = builder.CreatePtrOffset(dataRange.BasePtr, iv.Offset);
                    //Header:
                    //  T& currPtr = phi [Pred: startPtr], [Latch: {currPtr + iv.Scale}]
                    var currPtr = loop.Header.InsertPhi(startPtr.ResultType).SetName("lsr_ptr");
                    builder.SetPosition(latch);
                    currPtr.AddArg((prehdr, startPtr), (latch, builder.CreatePtrOffset(currPtr, iv.Scale)));

                    //Replace loop exit condition with `icmp.ult currPtr, endPtr` if not already.
                    if (shouldReplaceCond) {
                        var op = cond.Cmp!.Op.GetUnsigned();
                        cond.Cmp.ReplaceWith(new CompareInst(op, currPtr, dataRange.EndPtr!), insertIfInst: true);
                    }
                    //Replace indexed accesses with strength-reduced var
                    foreach (var user in array.Users()) {
                        if (user is not ArrayAddrInst acc || acc.Index != index || acc.IsCasting) continue;

                        acc.ReplaceWith(currPtr, insertIfInst: true);
                    }
                    numReduced++;
                    break;
                }
            }
        }
        return numReduced;
    }

    private static InductionVarMap FindInductionVars(LoopInfo loop, BasicBlock pred, BasicBlock latch)
    {
        var indVars = new InductionVarMap();
        var worklist = new ArrayStack<Instruction>();

        //Find basic IVs in the form `i = i + const`
        foreach (var phi in loop.Header.Phis()) {
            Debug.Assert(phi.NumArgs == 2);

            if (phi.GetValue(latch) is not BinaryInst { Op: BinaryOp.Add } update) continue;
            if (update.Left != phi || !loop.IsInvariant(update.Right)) continue;

            indVars.Add(phi, new() {
                Base = phi,
                Offset = phi.GetValue(pred),
                Scale = update.Right
            });

            //Enqueue users to the derived IV search worklist
            foreach (var user in phi.Users()) {
                if (user != update) {
                    worklist.Push(user);
                }
            }
        }

        //Find derived IVs
        while (worklist.TryPop(out var inst)) {
            if (indVars.ContainsKey(inst) || !CollectDerivedIV(inst)) continue;

            foreach (var user in inst.Users()) {
                worklist.Push(user);
            }
        }
        return indVars;

        bool CollectDerivedIV(Instruction inst)
        {
            switch (inst) {
                //DIV = &Array[IV]
                case ArrayAddrInst { Array: var source, Index: var index, IsCasting: false }
                when indVars.TryGetValue(index, out var indexIV) && loop.IsInvariant(source): {
                    indVars.TryAdd((source, index), indexIV);
                    return true;
                }
                case BinaryInst { Op: BinaryOp.Add or BinaryOp.Mul } bin: {
                    var iv = default(InductionVar);
                    if (MatchBinDerivedIV(bin.Op, bin.Left, bin.Right, ref iv) || 
                        MatchBinDerivedIV(bin.Op, bin.Right, bin.Left, ref iv)
                    ) {
                        indVars.Add(bin, iv);
                    }
                    return true;
                }
                case ConvertInst { IsTruncation: false, Value: var val } conv
                when indVars.TryGetValue(val, out var baseIV): {
                    indVars.Add(conv, baseIV);
                    return true;
                }
            }
            return false;
        }
        bool MatchBinDerivedIV(BinaryOp op, Value a, Value b, ref InductionVar div)
        {
            if (!indVars.TryGetValue(a, out div)) {
                return false;
            }
            //DIV = IV * C
            if (op == BinaryOp.Mul && div.Scale is ConstInt baseScale && b is ConstInt otherScale) {
                div.Scale = ConstInt.Create(otherScale.ResultType, baseScale.Value * otherScale.Value);
                return true;
            }
            //DIV = IV + x
            if (op is BinaryOp.Add && div.Offset is ConstInt { Value: 0 } && loop.IsInvariant(b)) {
                div.Offset = b;
                return true;
            }
            //DIV = IV + C
            if (op is BinaryOp.Add && div.Offset is ConstInt baseOffset && b is ConstInt otherOffset) {
                int sign = op == BinaryOp.Sub ? -1 : +1;
                div.Offset = ConstInt.Create(otherOffset.ResultType, baseOffset.Value + otherOffset.Value * sign);
                return true;
            }
            //baseIV operand is not a constant.
            //Note that supporting non-constant factors would require a more complex IV representation (SCEV?).
            return false;
        }
    }

    //Detects and returns the comparison, array/source, and index for a canonical foreach-like loop:
    //  while (i < source.Length) { ... }
    private static (CompareInst? Cmp, Value? Source, Value Index) GetCanonicalForeachLoopCond(LoopInfo loop)
    {
        var cond = loop.GetExitCondition();

        //Match cond is {icmp.slt i, (conv.i32 (arrlen array))}
        if (cond?.Op == CompareOp.Slt && 
            cond.Right is ConvertInst { Value: IntrinsicInst bound, ResultType.Kind: TypeKind.Int32 } &&
            bound.Is(CilIntrinsicId.ArrayLen)
        ) {
            return (cond, bound.Args[0], cond.Left);
        }
        if (cond?.Op is CompareOp.Slt or CompareOp.Ult) {
            return (cond, null, cond.Left);
        }
        return default;
    }

    /// <summary>
    /// Builds a sequence accessing the underlying ref and count from <paramref name="source"/>, assuming its type is one of:
    /// <see cref="ArrayType"/>, <see cref="string"/>, or <see cref="List{T}"/>.
    /// </summary>
    /// <remarks>
    /// Users should ensure exact types before calling this method, as it matches by name and thus could incorrectly 
    /// match some unrelated type named "List`1".
    /// </remarks>
    public static (Value BasePtr, Value? EndPtr, Value? Count) CreateGetDataPtrRange(IRBuilder builder, Value source, bool getCount = true)
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
        //T& endPtr = startPtr + (nuint)count * sizeof(T)
        var endPtr = getCount ? builder.CreatePtrOffset(basePtr, count!) : null;
        return (basePtr, endPtr, count);

        static CallInst CreateGetArrayDataRef(IRBuilder builder, Value source)
        {
            var elemType = ((ArrayType)source.ResultType).ElemType;
            var T0 = new GenericParamType(0, isMethodParam: true);

            var m_GetArrayDataRef = builder.Resolver
                .Import(typeof(System.Runtime.InteropServices.MemoryMarshal))
                .FindMethod("GetArrayDataReference", new MethodSig(T0.CreateByref(), new TypeSig[] { T0.CreateArray() }, numGenPars: 1))
                .GetSpec(new GenericContext(methodArgs: new[] { elemType }));

            return builder.CreateCall(m_GetArrayDataRef, source);
        }
    }

    //Represents the linear progression of an induction variable, in the form of: `Base * Scale + Offset`
    internal struct InductionVar
    {
        public Value Base, Offset, Scale;

        public override string ToString() => $"({Base}) * {Scale} + {Offset}";
    }
}