namespace DistIL.Passes;

using DistIL.IR;

public abstract class Pass
{
    public abstract void Transform(Method method);
}

public abstract class RewritePass : Pass
{
    public override void Transform(Method method)
    {
        var ib = new IRBuilder();

        foreach (var block in method) {
            EnterBlock(block);
            foreach (var inst in block) {
                ib.Position = inst;
                var result = Transform(ib, inst);

                if (result != inst) {
                    inst.ReplaceWith(result);
                }
            }
            LeaveBlock(block);
        }
    }
    protected virtual void EnterBlock(BasicBlock block) { }
    protected virtual void LeaveBlock(BasicBlock block) { }

    protected virtual Value Transform(IRBuilder ib, Instruction inst) => inst;
}