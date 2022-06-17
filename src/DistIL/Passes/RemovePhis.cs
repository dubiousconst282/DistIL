namespace DistIL.Passes;

using DistIL.IR;

/// <summary> Remove phis (inefficiently) and creates variables for instructions being used outside their parent block. </summary>
//(I admit that most of this code was copied from LLVM's DemoteRegToMem)
public class RemovePhis : MethodPass
{
    public override void Run(MethodTransformContext ctx)
    {
        var worklist = new List<Instruction>();

        foreach (var block in ctx.Method) {
            SplitCriticalEdges(block);

            foreach (var inst in block) {
                if (IsUsedGlobally(inst)) {
                    worklist.Add(inst);
                }
            }
        }

        foreach (var inst in worklist) {
            DemoteInst(inst);
        }
        worklist.Clear();

        foreach (var block in ctx.Method) {
            foreach (var phi in block.Phis()) {
                worklist.Add(phi);
            }
        }
        foreach (var phi in worklist) {
            DemotePhi((PhiInst)phi);
        }
    }

    private void DemoteInst(Instruction inst)
    {
        var slot = new Variable(inst.ResultType);

        //Replace uses with reloads
        foreach (var user in inst.Users()) {
            //Reloads for phis must be placed on predecessor blocks
            //We don't (shouldn't) allow phis with duplicated blocks, so we don't check for them here.
            if (user is PhiInst phi) {
                for (int i = 0; i < phi.NumArgs; i++) {
                    if (phi.GetValue(i) != inst) continue;

                    var srcBlock = phi.GetBlock(i);
                    var load = new LoadVarInst(slot);
                    srcBlock.InsertBefore(srcBlock.Last, load);
                    phi.SetValue(i, load);
                }
            } else {
                var load = new LoadVarInst(slot);
                load.InsertBefore(user);
                user.ReplaceOperands(inst, load);
            }
        }
        //Insert the store after all phis
        var insertPos = inst;
        while (insertPos.IsHeader) {
            insertPos = insertPos.Next!;
        }
        var store = new StoreVarInst(slot, inst);
        store.InsertAfter(insertPos);
    }

    private void DemotePhi(PhiInst phi)
    {
        var slot = new Variable(phi.ResultType);

        foreach (var (pred, value) in phi) {
            if (value is Undef) continue;

            var store = new StoreVarInst(slot, value);
            store.InsertBefore(pred.Last);
        }
        var load = new LoadVarInst(slot);
        phi.ReplaceWith(load);
    }

    private bool IsUsedGlobally(Instruction inst)
    {
        foreach (var user in inst.Users()) {
            if (user.Block != inst.Block) {
                return true;
            }
        }
        return false;
    }

    //Insert intermediate blocks between critical predecessor edges.
    private void SplitCriticalEdges(BasicBlock block)
    {
        var preds = block.Preds;
        if (preds.Count < 2) return;

        for (int i = 0; i < preds.Count; i++) {
            var pred = preds[i];
            if (pred.Succs.Count < 2) continue;

            //Create an intermediate block jumping to block
            //(can't use SetBranch() because we're looping through the edges)
            var intermBlock = block.Method.CreateBlock(insertAfter: pred).SetName("CritEdge");
            intermBlock.InsertLast(new BranchInst(block));
            //Create `interm<->block` edge
            intermBlock.Succs.Add(block);
            preds[i] = intermBlock;
            //Create `pred<->interm` edge
            pred.Reconnect(block, intermBlock);

            //Redirect branches/phis to the intermediate block
            pred.Last.ReplaceOperands(block, intermBlock);
            foreach (var phi in block.Phis()) {
                phi.ReplaceOperands(pred, intermBlock);
            }
        }
    }
}