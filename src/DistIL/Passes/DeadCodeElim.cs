namespace DistIL.Passes;

using DistIL.IR;

public class DeadCodeElim : MethodPass
{
    public override void Run(MethodTransformContext ctx)
    {
        var visitedBlocks = new ValueSet<BasicBlock>();
        var pendingBlocks = new ArrayStack<BasicBlock>();

        void Push(BasicBlock block)
        {
            if (visitedBlocks.Add(block)) {
                pendingBlocks.Push(block);
            }
        }

        bool changed = true;

        while (changed) {
            changed = false;

            //Find reachable blocks with a depth first search
            Push(ctx.Method.EntryBlock);
            while (pendingBlocks.TryPop(out var block)) {
                //Remove unused instructions in reverse order, to handle uses by dead instructions.
                //Multiple passes will handle global dead instructions, although probably inefficiently.
                foreach (var inst in block.Reversed()) {
                    if (inst.NumUses == 0 && inst.SafeToRemove) {
                        inst.Remove();
                        changed = true;
                    }
                }
                //Enqueue successors
                foreach (var succ in block.Succs) {
                    Push(succ);
                }
            }
            //Remove unreachable blocks
            foreach (var block in ctx.Method) {
                if (visitedBlocks.Contains(block)) continue;

                //Rewrite phis of reachable blocks
                foreach (var succ in block.Succs) {
                    if (!visitedBlocks.Contains(succ)) continue;

                    foreach (var phi in succ.Phis()) {
                        phi.RemoveArg(block, removeTrivialPhi: true);
                    }
                }
                block.Remove();
            }

            if (changed) {
                ctx.InvalidateAll();
            }
            visitedBlocks.Clear();
        }
    }
}