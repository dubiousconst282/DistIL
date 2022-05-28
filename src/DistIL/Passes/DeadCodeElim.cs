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
                    if (inst.NumUses == 0 && inst.SafeToRemove) {
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

                //Rewrite phis
                foreach (var succ in block.Succs) {
                    foreach (var phi in succ.Phis()) {
                        phi.RemoveArg(block, removeTrivialPhi: true);
                    }
                }
                Assert(block.NumUses == 0);
                block.Remove();
            }
            visitedBlocks.Clear();
        }
    }
}