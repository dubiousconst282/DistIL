namespace DistIL.Passes;

using DistIL.IR.Utils;

public class SimplifyCFG : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        bool everChanged = false;

        // Canonicalization
        everChanged |= SinkReturnsAndJumpThreads(ctx.Compilation, ctx.Method);

        // Simplification
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

        // succ can't start with a phi/guard, nor loop on itself, and we must be its only predecessor
        if (br.Then is not { HasPhisOrGuards: false, NumPreds: 1 } succ || succ == block) return false;

        succ.MergeInto(block, replaceBranch: true);
        return true;
    }

    // goto x == 0 ? T : F  ->  goto x ? F : T
    // goto x != 0 ? T : F  ->  goto x ? T : F
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

    //  Block: ...; goto cond ? Then : Else
    //  Then:  ...; goto EndBlock
    //  Else:  ...; goto EndBlock
    //  EndBlock: int res = phi [Then -> x], [Else -> y]; ...
    // -->
    //  Block: ...; goto EndBlock
    //  EndBlock: int res = select cond ? x : y; ...
    private static bool ConvertToBranchless(BasicBlock block)
    {
        if (block.Last is not BranchInst { IsConditional: true } br) return false;

        var succT = GetUniqueSuccWithPhis(br.Then);
        var succF = GetUniqueSuccWithPhis(br.Else);
        // If-Then must have at least one end block
        if ((succT == null && br.Then != succF) || (succF == null && br.Else != succT)) return false;
        // If-Then-Else must have the same end block
        if (succT != null && succF != null && succT != succF) return false;

        // Limit to a single select per branch to avoid pessimizing the code
        var endBlock = succT ?? succF;
        if (endBlock == null || endBlock.Phis().Count() is not 1) return false;

        // Check if we can execute both branches (they have no side-effects and it's cheap to do so)
        if (succT != null && !CanSpeculate(br.Then)) return false;
        if (succF != null && !CanSpeculate(br.Else)) return false;

        // Merge branches to the end of `block`
        Speculate(br.Then, endBlock);
        Speculate(br.Else, endBlock);
        block.SetBranch(endBlock);

        // We need to preserve phi args if branches are reachable from somewhere else
        bool keepT = br.Then.NumPreds > 0;
        bool keepF = br.Else.NumPreds > 0;

        // Rewrite phis with selects
        foreach (var phi in endBlock.Phis()) {
            var select = CreateSelect(phi);
            select.InsertBefore(block.Last);

            if (phi.NumArgs <= 2 && !keepT && !keepF) {
                phi.ReplaceWith(select);
            } else {
                if (br.Then != endBlock && !keepT) phi.RemoveArg(br.Then);
                if (br.Else != endBlock && !keepF) phi.RemoveArg(br.Else);
                if (succT == null || succF == null) phi.RemoveArg(block);
                phi.AddArg(block, select);
            }
        }
        return true;

        static BasicBlock? GetUniqueSuccWithPhis(BasicBlock block)
        {
            return block.Last is BranchInst { IsJump: true, Then: var succ } && succ.Phis().Any() ? succ : null;
        }
        static bool CanSpeculate(BasicBlock block)
        {
            if ((block.NumPreds >= 2 && block.First is not BranchInst { IsJump: true }) || block.HasPhisOrGuards) return false;

            int budget = 40;

            foreach (var inst in block) {
                if (inst == block.Last) break;

                int cost = inst switch {
                    BinaryInst { Op: BinaryOp.Add or BinaryOp.Sub } => +10,
                    BinaryInst { Op: BinaryOp.And or BinaryOp.Or or BinaryOp.Xor } => +10,
                    BinaryInst { Op: BinaryOp.Mul } => +25,
                    CompareInst => +15,
                    SelectInst => +15,
                    _ => 1_000_000
                };
                if ((budget -= cost) < 0) {
                    return false;
                }
            }
            return true;
        }
        void Speculate(BasicBlock sideBlock, BasicBlock endBlock)
        {
            if (sideBlock != endBlock) {
                if (sideBlock.First is not BranchInst) {
                    Debug.Assert(sideBlock.NumPreds == 1);
                    sideBlock.MergeInto(block, redirectSuccPhis: false);
                } else if (sideBlock.NumPreds == 1) {
                    sideBlock.Remove();
                }
            }
        }
        Instruction CreateSelect(PhiInst phi)
        {
            var valT = phi.GetValue(succT == null ? block : br.Then);
            var valF = phi.GetValue(succF == null ? block : br.Else!);

            if (br.Cond is CompareInst cond) {
                // select x ? 0 : y  ->  !x & y
                // select x ? 1 : y  ->   x | y
                // select x ? y : 0  ->   x & y
                // select x ? y : 1  ->  !x | y
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

    // Redirect all blocks ending with a `ret x` to a unique exit point, also collapse simple jump threads.
    private static bool SinkReturnsAndJumpThreads(Compilation comp, MethodBody method)
    {
        // There can't be multiple rets in methods with less than 3 blocks
        if (method.ReturnType == PrimType.Void || method.NumBlocks < 3) return false;

        var exitBlocks = new List<BasicBlock>();

        foreach (var block in method) {
            if (block.Last is ReturnInst) {
                exitBlocks.Add(block);
            } else {
                SimpleJumpThread(block);
            }
        }

        if (exitBlocks.Count < 2) return false;

        var singleExit = method.CreateBlock().SetName("CanonExit");
        var phi = singleExit.InsertPhi(method.ReturnType);
        singleExit.SetBranch(new ReturnInst(phi));

        foreach (var block in exitBlocks) {
            var value = ((ReturnInst)block.Last).Value!;
            phi.AddArg(block, value);
            block.SetBranch(singleExit);

            SimpleJumpThread(block);
        }
        return true;
    }

    // Collapse simple jump threads. On return `block` may have been removed.
    //  BB_01: goto BB_02;
    //  BB_02: goto BB_03;
    //  ----
    //  BB_01: goto BB_03;
    private static bool SimpleJumpThread(BasicBlock block)
    {
        // Not the entry block, first inst is a jump
        if (block is not { NumPreds: > 0, First: BranchInst { IsJump: true, Then: var succ } br }) return false;

        // Only transform if we don't need to change phis too much
        // Also avoid removing gotos for handler entry blocks because handlers they can't have guards.
        if (!succ.HasPhisOrGuards || (block.NumPreds == 1 && !block.Preds.First().IsUsedByPhis && !block.IsHandlerEntry)) {
            foreach (var pred in block.Preds) {
                pred.RedirectSucc(block, succ);
                succ.RedirectPhis(block, pred);
            }
            block.Remove();
            return true;
        }
        return false;
    }

    // Convert a integer based switch into a lookup table (RVA)
    //  BB_Switch: switch x, [* => *SwitchSucc { goto End; }]
    //  BB_Merge: int r = phi [*SwitchSucc: y]
    //  ----
    //  BB_Merge: int r = load &s_RVA + (x < lim ? x : lim)
    //
    // This may also generate a bitmask test if possible:
    //  int result = (0b01010101 >> x) & 1;
    private static bool ConvertSwitchToLut(Compilation comp, BasicBlock block)
    {
        if (!comp.Settings.AssumeLittleEndian || block.Last is not SwitchInst sw) return false;

        var finalBlock = default(BasicBlock);
        int numUniqueTargets = 0;

        // Check that all targets have no other preds and a single jump to the same final block
        foreach (var caseBlock in sw.GetUniqueTargets()) {
            var succBlock = default(BasicBlock);

            if (caseBlock is { First: BranchInst { IsJump: true } br }) {
                if (!(caseBlock.NumUses < 2 || caseBlock.Preds.All(pred => pred == block))) return false;
                succBlock = br.Then;
            } else if (caseBlock is { First: PhiInst }) {
                succBlock = caseBlock;
            } else {
                return false;
            }
            if (succBlock != (finalBlock ??= succBlock)) return false;
            
            numUniqueTargets++;
        }
        // Final block must have no preds other than switch targets, and phis must have all const args
        if (finalBlock == null || finalBlock.NumPreds > numUniqueTargets) return false;
        if (!finalBlock.Phis().All(IsPhiWithConstArgs)) return false;

        var builder = new IRBuilder(finalBlock.FirstNonHeader, InsertionDir.Before);

        foreach (var phi in finalBlock.Phis()) {
            var storageType = GetEffectiveStorageType(phi);
            int stride = storageType.Kind.Size();
            var data = new byte[(sw.NumTargets + 1) * stride];

            for (int i = -1; i < sw.NumTargets; i++) {
                // Encoding the default case value in the last slot saves a bounds check
                int offset = (i < 0 ? sw.NumTargets : i) * stride;
                var target = sw.GetTarget(i);
                var value = phi.GetValue(target == finalBlock ? block : target);

                // Write value in little-endian order
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

        // Lastly, replace the switch with a jump to the final block
        sw.ReplaceWith(new BranchInst(finalBlock), insertIfInst: true);

        // Also remove unreachable blocks
        foreach (var target in sw.GetUniqueTargets()) {
            if (target == finalBlock) continue;
            
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