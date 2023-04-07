namespace DistIL.Passes;

using DistIL.IR.Utils;

public class SimplifyCFG : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        bool everChanged = false;

        //Canonicalization
        everChanged |= UnifyReturns(ctx.Method);

        //Simplification
        ctx.Method.TraverseDepthFirst(postVisit: block => {
            bool changed = true;

            while (changed) {
                changed = false;
                changed |= ConvertSwitchToLut(ctx.Compilation, block);
                changed |= MergeWithSucc(block);
                changed |= InvertCond(block);
                changed |= ConvertToBranchless(block);
                everChanged |= changed;
            }
        });

        return everChanged ? MethodInvalidations.ControlFlow : 0;
    }

    private static bool MergeWithSucc(BasicBlock block)
    {
        if (block.Last is not BranchInst { IsJump: true } br) return false;

        //succ can't start with a phi/guard, nor loop on itself, and we must be its only predecessor
        if (br.Then is not { HasHeader: false, NumPreds: 1 } succ || succ == block) return false;

        succ.MergeInto(block, replaceBranch: true);
        return true;
    }

    //goto x == 0 ? T : F  ->  goto x ? F : T
    //goto x != 0 ? T : F  ->  goto x ? T : F
    private static bool InvertCond(BasicBlock block)
    {
        if (block.Last is not BranchInst { IsConditional: true } br) return false;

        if (br.Cond is CompareInst { Op: CompareOp.Eq or CompareOp.Ne, Right: ConstInt { Value: 0 } } cond) {
            br.Cond = cond.Left;
            
            if (cond.Op == CompareOp.Eq) {
                (br.Then, br.Else) = (br.Else!, br.Then);
            }
            if (cond.NumUses == 0) {
                cond.Remove();
            }
            return true;
        }
        return false;
    }

    //Note that this currently only works with perfect diamond sub-graphs, 
    //and will fail to fully convert conditions like:
    //  (x >= 10 && x <= 20) || (x >= 50 && x <= 75)
    //This could be tackled with "path duplication" as described in chapter 16 of the SSA book.
    //
    //  Block: ...; goto cond ? Then : Else
    //  Then:  ...; goto Target
    //  Else:  ...; goto Target
    //  Target: int res = phi [Then -> x], [Else -> y]; ...
    // -->
    //  Block: ...; goto Target
    //  Target: int res = select cond ? x : y; ...
    private static bool ConvertToBranchless(BasicBlock block)
    {
        if (block.Last is not BranchInst { IsConditional: true } br) return false;
        
        //Match diamond branch
        if (br.Then is not { Last: BranchInst { IsJump: true, Then: var target } }) return false;
        if (br.Else is not { Last: BranchInst { IsJump: true, Then: var elseTarget } }) return false;

        //Limit to a single select per branch to avoid suboptimal codegen
        if (target != elseTarget || target.Phis().Count() is not 1) return false;
        if (!CanFlattenBranch(br.Then) || !CanFlattenBranch(br.Else)) return false;

        //Flatten branches
        br.Then.MergeInto(block, redirectSuccPhis: false);
        br.Else.MergeInto(block, redirectSuccPhis: false);
        block.SetBranch(target);

        //Rewrite phis with selects
        foreach (var phi in target.Phis()) {
            var select = CreateSelect(br, phi);
            select.InsertBefore(block.Last);

            if (phi.NumArgs <= 2) {
                phi.ReplaceWith(select);
            } else {
                phi.RemoveArg(br.Then, removeTrivialPhi: false);
                phi.RemoveArg(br.Else, removeTrivialPhi: false);
                phi.AddArg(block, select);
            }
        }
        return true;

        static bool CanFlattenBranch(BasicBlock block)
        {
            if (block.NumPreds >= 2 || block.HasHeader) return false;

            int cost = 0;

            foreach (var inst in block) {
                if (inst == block.Last) break;

                if (inst.HasSideEffects || ++cost > 2) {
                    return false;
                }
            }
            return true;
        }
        static Instruction CreateSelect(BranchInst br, PhiInst phi)
        {
            var valT = phi.GetValue(br.Then);
            var valF = phi.GetValue(br.Else!);

            if (br.Cond is CompareInst cond) {
                //select x ? 0 : y  ->  !x & y
                //select x ? 1 : y  ->   x | y
                //select x ? y : 0  ->   x & y
                //select x ? y : 1  ->  !x | y
                var (x, y) = (valT, valF) switch {
                    (ConstInt c, _) => (c, valF),
                    (_, ConstInt c) => (c, valT),
                    _ => default
                };
                if (x?.Value is 0 or 1 && (y.ResultType == PrimType.Bool || y is ConstInt { Value: 0 or 1 })) {
                    bool negCond = (x.Value == 0 && x == valT) || (x.Value != 0 && x == valF);

                    if (!negCond || cond.NumUses < 2) {
                        if (negCond) {
                            cond.Op = cond.Op.GetNegated();
                        }
                        var op = x.Value != 0 ? BinaryOp.Or : BinaryOp.And;
                        return new BinaryInst(op, cond, y);
                    }
                }
            }
            return new SelectInst(br.Cond!, valT, valF, phi.ResultType);
        }
    }

    //Redirect all blocks ending with a `ret x` to a unique exit point
    private static bool UnifyReturns(MethodBody method)
    {
        //There can't be multiple rets in methods with less than 3 blocks
        if (method.ReturnType == PrimType.Void || method.NumBlocks < 3) return false;

        var exitBlocks = new List<BasicBlock>();

        foreach (var block in method) {
            if (SimpleJumpThread(block)) continue;
            
            if (block.Last is ReturnInst) {
                exitBlocks.Add(block);
            }
        }

        if (exitBlocks.Count < 2) return false;

        var singleExit = method.CreateBlock().SetName("CanonExit");
        var phi = singleExit.InsertPhi(method.ReturnType);

        foreach (var block in exitBlocks) {
            var value = ((ReturnInst)block.Last).Value!;
            phi.AddArg(block, value);
            block.SetBranch(singleExit);
        }
        singleExit.SetBranch(new ReturnInst(phi));
        return true;
    }

    //Colapse simple jump threads. On return `block` will have been removed.
    //  BB_01: goto BB_02;
    //  BB_02: goto BB_03;
    //  ----
    //  BB_01: goto BB_03;
    private static bool SimpleJumpThread(BasicBlock block)
    {
        if (block.First is not BranchInst { IsJump: true, Then: var succ } br) return false;

        //Only transform if we don't need to change phis, and the block is not the method entry.
        if (!succ.HasHeader && block.NumPreds > 0) {
            foreach (var pred in block.Preds) {
                pred.RedirectSucc(block, succ);
            }
            block.Remove();
            return true;
        }
        return false;
    }

    //Convert a integer based switch into a lookup table (RVA)
    //  BB_Switch: switch x, [* => *SwitchSucc { goto End; }]
    //  BB_Merge: int r = phi [*SwitchSucc: y]
    //  ----
    //  BB_Merge: int r = load &s_RVA + (x < lim ? x : lim)
    //
    //This may also generate a bitmask test if possible:
    //  int result = (0b01010101 >> x) & 1;
    private static bool ConvertSwitchToLut(Compilation comp, BasicBlock block)
    {
        if (!comp.Settings.AssumeLittleEndian || block.Last is not SwitchInst sw) return false;

        var finalBlock = default(BasicBlock);
        int numUniqueTargets = 0;

        //Check that all targets have no other preds and a single jump to the same final block
        foreach (var caseBlock in sw.GetUniqueTargets()) {
            if (caseBlock is not { First: BranchInst { IsJump: true } br }) return false;
            if (!(caseBlock.NumUses < 2 || caseBlock.Preds.All(pred => pred == block))) return false;
            if (br.Then != (finalBlock ??= br.Then)) return false;

            numUniqueTargets++;
        }
        //Final block must have no preds other than switch targets, and phis must have all const args
        if (finalBlock == null || finalBlock.NumPreds > numUniqueTargets) return false;
        if (!finalBlock.Phis().All(IsPhiWithConstArgs)) return false;

        var builder = new IRBuilder(finalBlock.FirstNonHeader, InsertionDir.Before);

        foreach (var phi in finalBlock.Phis()) {
            var storageType = GetEffectiveStorageType(phi);
            int stride = storageType.Kind.Size();
            var data = new byte[(sw.NumTargets + 1) * stride];

            for (int i = -1; i < sw.NumTargets; i++) {
                //Encoding the default case value in the last slot saves a bounds check
                int offset = (i < 0 ? sw.NumTargets : i) * stride;
                var value = phi.GetValue(sw.GetTarget(i));

                //Write value in little-endian order
                long bits = value switch {
                    ConstInt c => c.Value,
                    ConstFloat c => BitConverter.DoubleToInt64Bits(c.Value)
                };
                for (int j = 0; j < stride; j++) {
                    data[offset + j] = (byte)(bits >> (j * 8));
                }
            }

            //  uint index = min((uint)switchVal, numCases)
            var unboundedIndex = builder.CreateConvert(sw.TargetIndex, PrimType.Int32);
            var index = builder.CreateMin(unboundedIndex, ConstInt.CreateI(sw.NumTargets), unsigned: true);
            Value result;

            if (stride == 1 && data.Length <= 64 && data.All(b => b is 0 or 1)) {
                //  int result = (0b101010 >>> index) & 1
                long mask = 0;
                for (int i = 0; i < data.Length; i++) {
                    mask |= (long)data[i] << i;
                }
                result = builder.CreateConvert(
                    builder.CreateAnd(builder.CreateShrl(ConstInt.CreateL(mask), index), ConstInt.CreateL(1)),
                    phi.ResultType
                );
            } else {
                //  class Aux { public static BlockN FieldRVA = [...data] }
                //  T result = load &FieldRVA + index * sizeof(T)
                var fieldRva = comp.CreateStaticRva(data);
                var addr = builder.CreatePtrOffset(builder.CreateFieldAddr(fieldRva), index, storageType);
                result = builder.CreateLoad(addr);
            }
            phi.ReplaceWith(result);
        }

        //Lastly, replace the switch with a jump to the final block
        sw.ReplaceWith(new BranchInst(finalBlock), insertIfInst: true);

        //Also remove unreachable blocks
        foreach (var target in sw.GetUniqueTargets()) {
            Debug.Assert(target.NumPreds == 0);
            target.Remove();
        }

        return true;

        static bool IsPhiWithConstArgs(PhiInst phi)
        {
            for (int i = 0; i < phi.NumArgs; i++) {
                if (phi.GetValue(i) is not (ConstInt or ConstFloat)) {
                    return false;
                }
            }
            return true;
        }
        static TypeDesc GetEffectiveStorageType(PhiInst phi)
        {
            if (phi.ResultType.StackType == StackType.Int) {
                var types = phi.ResultType.Kind.IsSigned()
                    ? new[] { PrimType.SByte, PrimType.Int16, PrimType.Int32 }
                    : new[] { PrimType.Byte, PrimType.UInt16, PrimType.UInt32 };
                int rank = 0;

                for (int i = 0; i < phi.NumArgs; i++) {
                    var cons = (ConstInt)phi.GetValue(i);

                    while (rank < types.Length && !cons.FitsInType(types[rank])) {
                        rank++;
                    }
                }
                return types[rank];
            }
            return phi.ResultType;
        }
    }
}