namespace DistIL.Passes;

using DistIL.IR;

public class DeadCodeElim : MethodPass
{
    public override void Transform(MethodBody method)
    {
        var visitedBlocks = new HashSet<BasicBlock>();
        var pendingBlocks = new ArrayStack<BasicBlock>();

        bool changed = true;

        while (changed) {
            changed = false;

            pendingBlocks.Push(method.EntryBlock);
            while (pendingBlocks.TryPop(out var block)) {
                //Remove unused instructions
                foreach (var inst in block.Reversed()) {
                    if (inst.Uses.Count == 0 && inst.SafeToRemove) {
                        inst.Remove();
                        changed = true;
                    }
                }
                //DFS successors
                foreach (var succ in block.Succs) {
                    if (visitedBlocks.Add(succ)) {
                        pendingBlocks.Push(succ);
                    }
                }
            }
            //Remove unreachable blocks
            foreach (var block in method) {
                if (block == method.EntryBlock || visitedBlocks.Contains(block)) continue;

                //Rewrite phi
                foreach (var use in block.Uses.ToArray()) {
                    if (use.Inst is PhiInst phi) {
                        phi.RemoveArg(block, removeTrivialPhi: true);
                    } else {
                        Assert(!visitedBlocks.Contains(use.Inst.Block)); //must be a branch in another unreachable block
                    }
                }
                block.Uses.Clear();
                block.Remove();
            }
            visitedBlocks.Clear();
        }
    }
}