namespace DistIL.Passes;

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
                    //TODO: remove variables whose users are all stores
                    if (inst is StoreVarInst { Var.NumUses: 1 }) {
                        inst.Remove();
                    }
                }
                //`goto 1 ? T : F`  ->  `goto T`
                if (block.Last is BranchInst { Cond: ConstInt { Value: var cond } } br) {
                    var (blockT, blockF) = cond != 0 ? (br.Then, br.Else!) : (br.Else!, br.Then);
                    
                    blockF.RedirectPhis(block, newPred: null);
                    block.SetBranch(blockT);
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
                    if (visitedBlocks.Contains(succ)) {
                        succ.RedirectPhis(block, newPred: null);
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