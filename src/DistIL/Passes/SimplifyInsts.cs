namespace DistIL.Passes;

using DistIL.IR.Utils;

using Bin = IR.BinaryOp;
using CallOptEntry = ValueTuple<TypeDesc?, Func<MethodTransformContext, CallInst, bool>>;
using Cmp = IR.CompareOp;

/// <summary> Implements peepholes/combining/scalar transforms that don't affect control flow. </summary>
public partial class SimplifyInsts : MethodPass
{
    readonly Dictionary<string, CallOptEntry[]> _callOpts = new();

    public SimplifyInsts(ModuleDef mod)
    {
#pragma warning disable format
        AddCallOpt(typeof(Delegate),        "Invoke",           DirectizeLambda);
        AddCallOpt(typeof(Dictionary<,>),   "ContainsKey",      SimplifyDictLookup);
#pragma warning restore format

        void AddCallOpt(Type declType, string methodName, Func<MethodTransformContext, CallInst, bool> run)
        {
            if (mod.Resolver.Import(declType) is { } declTypeDesc) {
                ref var entries = ref _callOpts.GetOrAddRef(methodName);
                entries = new CallOptEntry[(entries?.Length ?? 0) + 1];
                entries[^1] = (declTypeDesc, run);
            }
        }
    }

    public override void Run(MethodTransformContext ctx)
    {
        foreach (var inst in ctx.Method.Instructions()) {
            var newValue = inst switch {
                BinaryInst c    => SimplifyBinary(c),
                CompareInst c   => SimplifyCompare(c),
                UnaryInst c     => SimplifyUnary(c),
                ConvertInst c   => SimplifyConvert(c),
                CallInst c      => SimplifyCall(ctx, c),
                _ => null
            };
            if (newValue != null) {
                inst.ReplaceWith(newValue);
            }
        }
    }

    private Value? SimplifyCall(MethodTransformContext ctx, CallInst call)
    {
        var method = call.Method;

        if (_callOpts.TryGetValue(method.Name, out var entries)) {
            foreach (var (reqType, run) in entries) {
                var declType = method.DeclaringType;
                if (declType is TypeSpec spec && reqType is TypeDef) {
                    declType = spec.Definition;
                }
                if (reqType != null && !declType.Inherits(reqType)) continue;

                if (run(ctx, call)) {
                    return null;
                }
            }
        }
        return ConstFolding.FoldCall(call.Method, call.Args);
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
        return ConstFolding.FoldBinary(inst.Op, inst.Left, inst.Right);
    }

    private Value? SimplifyCompare(CompareInst inst)
    {
        //(const op x)  ->  (x swapped_op const)
        if (inst is { Left: Const, Right: not Const }) {
            inst.Op = inst.Op.GetSwapped();
            (inst.Left, inst.Right) = (inst.Right, inst.Left);
        }
        //((x op y) == 0)  ->  (x !op y)
        //((x op y) != 0)  ->  (x op y)
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
}