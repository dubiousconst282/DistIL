namespace DistIL.Passes;

using DistIL.IR.Utils;

using Bin = IR.BinaryOp;
using Cmp = IR.CompareOp;

/// <summary> Implements peepholes/combining/scalar transforms that don't affect control flow. </summary>
public partial class SimplifyInsts : IMethodPass
{
    readonly TypeDefOrSpec t_Delegate, t_DictionaryOfTT;

    public SimplifyInsts(ModuleResolver resolver)
    {
        t_Delegate = resolver.Import(typeof(Delegate));
        t_DictionaryOfTT = resolver.Import(typeof(Dictionary<,>), throwIfNotFound: false);
    }

    static IMethodPass IMethodPass.Create<TSelf>(Compilation comp)
        => new SimplifyInsts(comp.Module.Resolver);

    public MethodPassResult Run(MethodTransformContext ctx)
    {
        foreach (var inst in ctx.Method.Instructions()) {
            var newValue = inst switch {
                BinaryInst c    => SimplifyBinary(c),
                CompareInst c   => SimplifyCompare(c),
                UnaryInst c     => SimplifyUnary(c),
                ConvertInst c   => SimplifyConvert(c),
                CallInst c      => SimplifyCall(c),
                IntrinsicInst c => SimplifyIntrinsic(c),
                _ => null
            };
            if (newValue != null) {
                inst.ReplaceWith(newValue, insertIfInst: true);
            }
        }
        //TODO: track invalidations precisely
        return MethodInvalidations.DataFlow;
    }

    private Value? SimplifyCall(CallInst call)
    {
        var method = call.Method;
        var declType = method.DeclaringType;

        bool changed = method.Name switch {
            "Invoke" when declType.Inherits(t_Delegate)
                => DevirtualizeLambda(call),

            "ContainsKey" when declType is TypeDefOrSpec s && s.Definition == t_DictionaryOfTT
                => SimplifyDictLookup(call),

            _ => false
        };

        return changed ? null : ConstFolding.FoldCall(call.Method, call.Args);
    }

    private Value? SimplifyIntrinsic(IntrinsicInst inst)
    {
        return ConstFolding.FoldIntrinsic(inst);
    }

    private Value? SimplifyBinary(BinaryInst inst)
    {
        //(const op x)  ->  (x op const), if op is commutative
        if (inst is { Left: Const, Right: not Const, IsCommutative: true } b) {
            (b.Left, b.Right) = (b.Right, b.Left);
        }
        //((x op const) op const)  ->  (x op (const op const)), if op is associative
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
        if (inst.ResultType.IsPointerLike() && SimplifyAddress(inst) is { } addr) {
            return addr;
        }
        return ConstFolding.FoldBinary(inst.Op, inst.Left, inst.Right);
    }

    private Value? SimplifyCompare(CompareInst inst)
    {
        //(const op x)  ->  (x swapped_op const)
        //(x op cmp)    ->  (cmp op x)
        if (inst is { Left: Const, Right: not Const } or { Left: not CompareInst, Right: CompareInst }) {
            inst.Op = inst.Op.GetSwapped();
            (inst.Left, inst.Right) = (inst.Right, inst.Left);
        }
        //((x op y) == 0)  ->  (x !op y)
        //((x op y) != 0)  ->  (x op y)
        //((x op y) == 1)  ->  (x op y)
        //((x op y) != 1)  ->  (x !op y)
        if (inst is {
            Op: Cmp.Eq or Cmp.Ne,
            Left: CompareInst { NumUses: 1 } lhs,
            Right: (ConstInt or ConstNull) and var rhs
        }) {
            bool predNe = inst.Op == Cmp.Ne;
            bool isRhsOne = rhs is ConstInt { Value: not 0 };

            if (rhs is ConstInt { Value: not (0 or 1) } ) {
                return ConstInt.CreateI(predNe ? 1 : 0);
            }
            if (predNe == isRhsOne) {
                lhs.Op = lhs.Op.GetNegated();
            }
            return lhs;
        }
        return ConstFolding.FoldCompare(inst.Op, inst.Left, inst.Right);
    }

    private Value? SimplifyUnary(UnaryInst inst)
    {
        // -(-x)  ->  x
        if (inst.Value is UnaryInst sub && sub.Op == inst.Op && 
            inst.Op is UnaryOp.Neg or UnaryOp.Not or UnaryOp.FNeg
        ) {
            return sub;
        }
        return ConstFolding.FoldUnary(inst.Op, inst.Value);
    }

    private Value? SimplifyConvert(ConvertInst inst)
    {
        return ConstFolding.FoldConvert(inst.Value, inst.ResultType, inst.CheckOverflow, inst.SrcUnsigned);
    }
    //r5 = conv r18 -> long         (?)
    //r6 = mul r5, stride -> long
    //r7 = conv r6 -> nint          (?)
    //r8 = add basePtr, r7 -> nint
    //
    // -> lea basePtr + r18 * stride
    private static Value? SimplifyAddress(BinaryInst? inst)
    {
        if (!IRMatcher.Add(inst, out var basePtr, out var disp)) return null;

        if (disp is ConvertInst { ResultType.StackType: StackType.NInt } conv1) {
            disp = conv1.Value;
        }
        if (!IRMatcher.Mul(disp, out var index, out var stride)) {
            //Byte addressing
            return new PtrOffsetInst(basePtr, disp, stride: 1);
        }

        if (index is ConvertInst { IsTruncation: false } conv2) {
            index = conv2.Value;
        }

        if (stride is CilIntrinsic.SizeOf sz) {
            return new PtrOffsetInst(basePtr, index, sz.ObjType);
        } else if (stride is ConstInt cstride) {
            return new PtrOffsetInst(basePtr, index, (int)cstride.Value);
        }
        return null;
    }
}