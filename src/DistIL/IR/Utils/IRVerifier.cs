namespace DistIL.IR.Utils;

using DistIL.Analysis;

public class IRVerifier
{
    readonly MethodBody _method;
    readonly List<Diagnostic> _diags = new();

    private IRVerifier(MethodBody method)
    {
        _method = method;
    }

    public static List<Diagnostic> Diagnose(MethodBody method)
    {
        var v = new IRVerifier(method);
        v.VerifyInstsAndUses();
        return v._diags;
    }

    private void VerifyInstsAndUses()
    {
        Check(_method.EntryBlock.NumPreds == 0, _method.EntryBlock, "Entry block should not have predecessors");

        var useChecker = new UseChecker();

        foreach (var block in _method) {
            int blockInstIdx = 0;

            foreach (var inst in block) {
                CheckInst(inst);
                useChecker.AddInst(this, inst, blockInstIdx++);
            }
            Check(block.Last.IsBranch, block, "Block must end with a valid terminator");
        }
        useChecker.Validate(this);
    }

    private void CheckInst(Instruction inst)
    {
        switch (inst) {
            case GuardInst guard: {
                Check(
                    guard.HandlerBlock.NumPreds == 1 && (!guard.HasFilter || guard.FilterBlock.NumPreds == 1), guard,
                    "Guard handler/filter block must have a single predecessor");
                Check(
                    guard.Prev is null or GuardInst, guard,
                    "Guards must come before phi and normal instructions");
                break;
            }
            case PhiInst phi: {
                Check(
                    phi.Prev is null or GuardInst or PhiInst, phi,
                    "Phis must come before normal instructions");

                var preds = new HashSet<BasicBlock>();
                foreach (var arg in phi) {
                    Check(preds.Add(arg.Block), phi, "Phi should not have duplicated block arguments");
                }
                preds.SymmetricExceptWith(phi.Block.Preds.AsEnumerable());
                Check(preds.Count == 0, phi, "Phi must have one argument for each block predecessor");
                break;
            }
            case StoreVarInst { Var.ResultType: var dstType, Value.ResultType: var srcType, Value: var srcVal }: {

                if (!srcType.IsStackAssignableTo(dstType)) {
                    Error(inst, $"Store to incompatible type: {srcType} -> {dstType}", DiagnosticSeverity.Warn);
                } else if (
                    dstType.StackType == StackType.Int && srcType.Kind > dstType.Kind &&
                    !(dstType.Kind == TypeKind.Bool && srcVal is CompareInst or ConstInt { Value: 0 or 1 })
                ) {
                    Error(inst, $"Coerced store will implicitly truncate source value: {srcType} -> {dstType}", DiagnosticSeverity.Info);
                }
                break;
            }
            case { IsBranch: true, Next: not null }: {
                Error(inst, "Branch must be the last instruction in the block");
                break;
            }
        }
    }

    private void Error(Value location, string msg, DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        _diags.Add(new Diagnostic() {
            Severity = severity,
            Location = location,
            Message = msg
        });
    }
    private void Check(bool cond, Value location, string msg, DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        if (!cond) {
            Error(location, msg, severity);
        }
    }

    class UseChecker
    {
        Dictionary<TrackedValue, HashSet<Instruction>> _expectedUsers = new();
        Dictionary<Instruction, int> _blockIndices = new();

        public void AddInst(IRVerifier verifier, Instruction inst, int blockIndex)
        {
            foreach (var oper in inst.Operands) {
                if (oper is Instruction instOper && instOper.Block?.Method != verifier._method) {
                    verifier.Error(inst, "Using an instruction that was removed or is declared outside the parent method");
                } else if (oper is TrackedValue trackedOper) {
                    var expUsers = _expectedUsers.GetOrAddRef(trackedOper) ??= new();
                    expUsers.Add(inst);
                }

                if (oper is Variable && inst is not VarAccessInst) {
                    verifier.Error(inst, "Variables should only be used as operands by VarAccessInst (unless after RemovePhis)", DiagnosticSeverity.Warn);
                }
            }
            _blockIndices[inst] = blockIndex;
        }

        public void Validate(IRVerifier verifier)
        {
            var domTree = new DominatorTree(verifier._method);
            var actUsers = new HashSet<Instruction>();

            foreach (var (val, expUsers) in _expectedUsers) {
                //Check use list correctness
                actUsers.UnionWith(val.Users().AsEnumerable());
                actUsers.SymmetricExceptWith(expUsers);
                verifier.Check(actUsers.Count == 0, val, "Invalid value use chain");
                actUsers.Clear();

                //Check dominance
                if (val is Instruction defInst) {
                    foreach (var user in defInst.Users()) {
                        if (user is Instruction userInst && !IsDominatedByDef(domTree, defInst, userInst)) {
                            verifier.Error(user, $"Using non-dominating instruction '{defInst}'");
                        }
                    }
                }
            }
        }

        private bool IsDominatedByDef(DominatorTree domTree, Instruction def, Instruction user)
        {
            if (user is PhiInst phi) {
                foreach (var (pred, value) in phi) {
                    if (value == def && !domTree.Dominates(def.Block, pred)) {
                        return false;
                    }
                }
                return true;
            }
            return user.Block == def.Block
                ? _blockIndices[def] < _blockIndices[user]
                : domTree.Dominates(def.Block, user.Block);
        }
    }
}

public struct Diagnostic
{
    public DiagnosticSeverity Severity { get; init; }
    /// <summary> The BasicBlock or Instruction originating this diagnostic. </summary>
    public Value Location { get; init; }
    public string? Message { get; init; }

    public override string ToString()
    {
        return $"[{Severity}] {Message} ({Location})";
    }
}
public enum DiagnosticSeverity
{
    Info,
    Warn,
    Error,
}