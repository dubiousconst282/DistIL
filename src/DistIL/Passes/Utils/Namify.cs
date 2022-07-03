namespace DistIL.Passes.Utils;

using DistIL.IR;

/// <summary> Generate names for anonymous (unnamed) blocks and instructions to allow easy diffing. </summary>
public class NamifyIR : MethodPass
{
    public override void Run(MethodTransformContext ctx)
    {
        Run(ctx.Method);
        ctx.PreserveAll();
    }

    public static void Run(MethodBody body)
    {
        var symTable = body.GetSymbolTable();
        int blockId = 0;
        int instId = 0;

        foreach (var block in body) {
            if (!symTable.HasCustomName(block)) {
                symTable.SetName(block, $"{GetBaseName(block)}Block{++blockId}");
            }

            foreach (var inst in block) {
                if (inst.HasResult && !symTable.HasCustomName(inst)) {
                    symTable.SetName(inst, $"{GetBaseName(inst)}{++instId}");
                }
            }
        }
    }

    private static string GetBaseName(BasicBlock block)
    {
        if (block == block.Method?.EntryBlock) {
            return "Entry";
        }
        if (block.Last is ReturnInst) {
            return "Exit";
        }
        if (block.Last is BranchInst { IsConditional: true }) {
            return "Cond";
        }
        return "";
    }
    private static string GetBaseName(Instruction inst)
    {
        if (inst is CompareInst cmp) return $"{cmp.Op.ToString().ToLower()}";
        if (inst is BinaryInst bin) {
            if (bin.Right is ConstInt { Value: 1 } && bin.Op is BinaryOp.Add or BinaryOp.Sub) {
                return bin.Op == BinaryOp.Add ? "inc" : "dec";
            }
            return bin.Op.ToString().ToLower();
        }
        if (inst is CallInst call) {
            return call.Method.Name;
        }
        if (inst is LoadFieldInst fld) {
            return fld.Field.Name;
        }
        if (inst is PhiInst) return "phi";
        return "i";
    }
}