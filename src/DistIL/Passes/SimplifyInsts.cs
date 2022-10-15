namespace DistIL.Passes;

using DistIL.IR.Utils;

using Bin = IR.BinaryOp;
using Cmp = IR.CompareOp;

/// <summary> Implements peepholes/combining/scalar transforms that don't affect control flow. </summary>
public partial class SimplifyInsts : MethodPass
{
    readonly TypeDesc? t_Delegate;

    public SimplifyInsts(ModuleDef mod)
    {
        t_Delegate = mod.Import(typeof(Delegate));
    }

    public override void Run(MethodTransformContext ctx)
    {
        int itr = 0;

        bool changed = true;
        while (changed) {
            changed = false;

            foreach (var inst in ctx.Method.Instructions()) {
                changed |= inst switch {
                    BinaryInst c => TrySimplifyBinary(c),
                    CompareInst c => TrySimplifyCompare(c),
                    CallInst c => TrySimplifyCall(c),
                    _ => false
                };
            }
            Ensure.That(itr++ < 128, "SimplifyInsts got stuck in an infinite loop");
        }
    }

    private bool TrySimplifyCall(CallInst call)
    {
        if (DirectizeLambda(call)) return true;

        return false;
    }

    private bool TrySimplifyBinary(BinaryInst inst)
    {
        if (SimplifyBinary(inst) is Value folded) {
            inst.ReplaceWith(folded);
            return true;
        }
        return false;
    }
    private Value? SimplifyBinary(BinaryInst inst)
    {
        //`const op x`  ->  `x op const`, if op is commutative
        if (inst is { Left: Const, Right: not Const, IsCommutative: true } b) {
            (b.Left, b.Right) = (b.Right, b.Left);
        }
        //`((x op const) op const)`  ->  `(x op (const op const))`, if op is associative
        if (inst is {
            Left: BinaryInst { Left: not Const, Right: Const } l_nc_c,
            Right: Const,
            IsAssociative: true
        } &&
            l_nc_c.Op == inst.Op &&
            ConstFolding.FoldBinary(inst.Op, l_nc_c.Right, inst.Right) is Value lr_op_r
        ) {
            inst.Left = l_nc_c.Left;
            inst.Right = lr_op_r;
        }
        return ConstFolding.FoldBinary(inst.Op, inst.Left, inst.Right);
    }

    private bool TrySimplifyCompare(CompareInst inst)
    {
        if (SimplifyCompare(inst) is Value folded) {
            inst.ReplaceWith(folded);
            return true;
        }
        return false;
    }
    private Value? SimplifyCompare(CompareInst inst)
    {
        //(x op y) eq 0  ->  (x !op y)
        //(x op y) ne 0  ->  (x op y)
        if (inst is {
            Op: Cmp.Eq or Cmp.Ne,
            Left: CompareInst { NumUses: 1 },
            Right: ConstInt { Value: 0 } or ConstNull
        }) {
            bool neg = inst.Op == Cmp.Eq;
            inst = (CompareInst)inst.Left;
            if (neg) inst.Op = inst.Op.GetNegated();
        }
        return ConstFolding.FoldCompare(inst.Op, inst.Left, inst.Right);
    }
}