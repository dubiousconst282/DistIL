namespace DistIL.Passes;

using DistIL.IR;

public class DeadCodeElim : MethodPass
{
    public override void Run(MethodTransformContext ctx)
    {
        var visitedBlocks = new RefSet<BasicBlock>();
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
                //`goto 1 ? T : F`  ->  `goto T`
                if (block.Last is BranchInst { Cond: ConstInt { Value: var cond } } br) {
                    block.SetBranch(cond != 0 ? br.Then : br.Else!);
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