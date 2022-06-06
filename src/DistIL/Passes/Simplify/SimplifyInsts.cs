namespace DistIL.Passes;

using DistIL.IR;

/// <summary> Implements peepholes/combining/scalar transforms that don't affect control flow. </summary>
public partial class SimplifyInsts : MethodPass
{
    private TypeDesc? t_Delegate;

    public override void Run(MethodTransformContext ctx)
    {
        t_Delegate = ctx.Module.GetImport(typeof(Delegate));

        foreach (var inst in ctx.Method.Instructions()) {
            bool changed = inst switch {
                CallInst c => SimplifyCall(c),
                _ => false
            };
        }
    }

    private bool SimplifyCall(CallInst call)
    {
        if (InlineLambda(call)) return true;

        return false;
    }
}