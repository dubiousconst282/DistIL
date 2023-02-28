namespace DistIL.Passes;

using DistIL.Analysis;
using DistIL.AsmIO;
using DistIL.IR.Utils;

public class LoopUnrolling : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var loopAnalysis = ctx.GetAnalysis<LoopAnalysis>(preserve: true);
        bool changed = false;

        foreach (var loop in loopAnalysis.GetInnermostLoops()) {
            if (LoopSnapshot.TryCapture(loop) is not { } snap) continue;

            int factor = PickUnrollFactor(snap);

            if (factor > 0 && CloneAndUnrollLoop(snap, factor)) {
                changed = true;
            }
        }
        
        return changed ? MethodInvalidations.Loops : 0;
    }

    private static int PickUnrollFactor(LoopSnapshot loop)
    {
        //Only consider short loops with a single body block (in which case the latch is the body)
        if (loop.Blocks.Count != 2) return 0;

        int cost = 0;
        int maxStoreSize = 0;

        foreach (var inst in loop.Latch) {
            cost += inst switch {
                BinaryInst => 1,
                CallInst call when IsMathClass(call.Method.DeclaringType) => 1,
                CallInst call => call.NumArgs + 3,
                _ => inst.HasSideEffects ? +4 : +2
            };
            if (cost > 32) return 0;

            if (inst is StoreInst store) {
                maxStoreSize = Math.Max(store.LocationType.Kind.BitSize(), maxStoreSize);
            }
        }
        //Use some factor based on some store width to hopefully
        //increase SLP vectorization opportunities
        return Math.Max(4, maxStoreSize > 0 ? 256 / maxStoreSize : 0);

        static bool IsMathClass(TypeDesc type)
        {
            return type.IsCorelibType() &&
                   type.Name is "Math" or "MathF" or "BitOperations" ||
                   type.Namespace is "System.Runtime.Intrinsics" or "System.Numerics";
        }
    }

    //Creates an unrolled clone of the specified counting loop:
    //Input:
    //  for (; i < n; i++) { A; }
    //
    //Output:
    //  for (; n - i >= UF; i += UF) { A; A; ... }
    //  for (; i < n; i++) { A; }
    public static bool CloneAndUnrollLoop(LoopSnapshot loop, int unrollFactor)
    {
        if (HasHeaderCarriedDeps(loop) || loop.Step is not ConstInt stepCons) return false;

        var carriedDefs = new List<(PhiInst OrigPhi, PhiInst ClonedPhi, Value LatchExit, Value Curr)>();
        var headerBranch = (BranchInst)loop.Header.Last;
        var origPredBlock = loop.Predecessor.Split(loop.Predecessor.Last); //pred for the original loop (after connection)

        var cloner = new IRCloner();
        cloner.SetFallbackRemapper(v => v, createNewVars: false); //keep non-explicitly mapped values
        cloner.AddMapping(origPredBlock, loop.Predecessor);
        cloner.AddMapping(headerBranch.Else!, origPredBlock); //exit to the original loop
        CloneLoop(insertAfter: loop.Predecessor);

        //Connect unrolled loop to the CFG
        var clonedHeader = cloner.GetMapping(loop.Header);
        var firstClonedLatch = cloner.GetMapping(loop.Latch);
        var lastClonedLatch = firstClonedLatch;
        var clonedExitCond = cloner.GetMapping(loop.Condition);

        loop.Predecessor.SetBranch(clonedHeader);

        foreach (var phi in loop.Header.Phis()) {
            var latchExit = phi.GetValue(loop.Latch);

            carriedDefs.Add((
                OrigPhi: phi,
                ClonedPhi: cloner.GetMapping(phi),
                latchExit,
                Curr: cloner.GetMapping(latchExit)
            ));
        }

        //Clone body desired amount times
        for (int i = 1; i < unrollFactor; i++) {
            cloner.Clear();

            foreach (ref var slot in carriedDefs.AsSpan()) {
                cloner.AddMapping(slot.OrigPhi, slot.Curr!);
            }
            cloner.AddMapping(loop.Header, clonedHeader);
            CloneLoop(insertAfter: lastClonedLatch, skipHeader: true);

            //Redirect back-edge of the previous unrolled iter to the body of the current one
            var firstBodyBlock = cloner.GetMapping(headerBranch.Then);
            lastClonedLatch.Last.ReplaceOperand(clonedHeader, firstBodyBlock);

            //Update carried defs
            //TODO: consider splitting reduction IVs for better OOE scheduling, see https://stackoverflow.com/a/2349265
            foreach (ref var slot in carriedDefs.AsSpan()) {
                slot.Curr = cloner.GetMapping(slot.LatchExit);
            }
            lastClonedLatch = cloner.GetMapping(loop.Latch);
        }
        //Rewrite header phis
        foreach (var (origPhi, clonedPhi, _, curr) in carriedDefs) {
            origPhi.SetValue(origPredBlock, clonedPhi);
            clonedPhi.RedirectArg(firstClonedLatch, lastClonedLatch, curr);
        }
        //Rewrite condition (icmp.lt i, n)  ->  (icmp.uge (sub n, i), unrollFactor * iStep)
        var remIters = new BinaryInst(BinaryOp.Sub, clonedExitCond.Right, clonedExitCond.Left);
        var remType = remIters.ResultType.StackType == StackType.Long ? PrimType.Int64 : PrimType.Int32;
        var newExitCond = new CompareInst(CompareOp.Uge, remIters, ConstInt.Create(remType, unrollFactor * stepCons.Value));
        remIters.InsertBefore(clonedExitCond);
        clonedExitCond.ReplaceWith(newExitCond, insertIfInst: true);

        return true;

        void CloneLoop(BasicBlock insertAfter, bool skipHeader = false)
        {
            var method = loop.Header.Method;

            foreach (var block in loop.Blocks) {
                if (skipHeader && block == loop.Header) continue;

                var newBlock = method.CreateBlock(insertAfter);
                cloner.AddBlock(block, newBlock);
                insertAfter = newBlock;
            }
            cloner.Run();
        }
    }

    //Checks if the loop body uses some non-phi instruction defined in the loop header
    private static bool HasHeaderCarriedDeps(LoopSnapshot loop)
    {
        foreach (var inst in loop.Header.NonPhis()) {
            foreach (var user in inst.Users()) {
                if (user.Block != loop.Header && loop.Contains(user.Block)) {
                    return true;
                }
            }
        }
        return false;
    }

    //In terms of IR, a counting loop looks like:
    //  Prehdr:
    //    goto Header
    //  Header:
    //    int i = phi [Prehdr: 0], [Body_Latch: i2]
    //    int sum = phi [Prehdr: 0], [Body_Latch: sum2]
    //    bool cond = icml.slt i, bound
    //    goto cond ? Body_Latch : Exit
    //  Body_Latch:
    //    int sum2 = add sum, i
    //    int i2 = add i, 1
    //    goto Header
    //
    //After unrolling, we should get this output:
    //  Prehdr:
    //    goto Un_Header
    //  Un_Header:
    //    int i = phi [Prehdr: 0], [Un_Body_Latch: iN]
    //    int sum = phi [Prehdr: 0], [Un_Body_Latch: sumN]
    //    int rem = sub bound, i        //cannot be avoided for gc refs, pretty cheap anyway
    //    bool cond = icmp.sge rem, UF
    //    goto cond ? Body_Latch : Header
    //  Un_Body_Latch:
    //    //Itr #1
    //    int sum2 = add sum, i
    //    int i2 = add i, 1
    //  
    //    //Itr #UF (=2)
    //    int sumN = add sum2, i2
    //    int iN = add i2, 1
    //  
    //    goto Un_Header
    //  
    //  Header:
    //   int orig_i = phi [Orig_Prehdr: i], ...
    //   ...
}