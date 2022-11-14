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
        v.VerifyBlocks();
        v.VerifyInstsAndUses();
        return v._diags;
    }

    private void VerifyBlocks()
    {
        Check(_method.EntryBlock.NumPreds == 0, _method.EntryBlock, "Entry block should not have predecessors");

        foreach (var block in _method) {
            Check(!(block.First is PhiInst && block.FirstNonPhi is GuardInst), block, "Block should not have both phis and guards");
            Check(block.Last.IsBranch, block, "Block must end with a valid terminator");
        }
    }

    private void VerifyInstsAndUses()
    {
        var valueUsers = new Dictionary<TrackedValue, HashSet<Instruction>>();
        var blockIndices = new Dictionary<Instruction, int>();
        var domTree = new DominatorTree(_method);

        foreach (var block in _method) {
            int index = 0;

            foreach (var inst in block) {
                foreach (var oper in inst.Operands) {
                    if (oper is Instruction instOper && instOper.Block?.Method != _method) {
                        Error(inst, "Using an instruction that was removed or is declared outside the parent method");
                    } else if (oper is TrackedValue trackedOper) {
                        var expUsers = valueUsers.GetOrAddRef(trackedOper) ??= new();
                        expUsers.Add(inst);
                    }

                    if (oper is Variable && inst is not VarAccessInst) {
                        Error(inst, "Variables should only be used as operands by VarAccessInst (unless after RemovePhis)", DiagnosticSeverity.Warn);
                    }
                }
                VerifyInst(inst);
                blockIndices[inst] = index++;
            }
        }
        
        foreach (var (val, expUsers) in valueUsers) {
            //Check use list correctness
            var actUsers = new HashSet<Instruction>(val.Users().AsEnumerable());
            actUsers.SymmetricExceptWith(expUsers);
            Check(actUsers.Count == 0, val, "Invalid value user set");

            //Check dominance
            if (val is Instruction defInst) {
                foreach (var user in defInst.Users()) {
                    if (user is Instruction userInst) {
                        Check(IsDominatedByDef(defInst, userInst), user, "Using non-dominating instruction");
                    }
                }
            }
        }
        bool IsDominatedByDef(Instruction def, Instruction user)
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
                ? blockIndices[def] < blockIndices[user] 
                : domTree.Dominates(def.Block, user.Block);
        }
    }

    private void VerifyInst(Instruction inst)
    {
        switch (inst) {
            case PhiInst phi: {
                var phiPreds = new HashSet<BasicBlock>();

                foreach (var arg in phi) {
                    Check(phiPreds.Add(arg.Block), phi, "Phi should not have duplicated block arguments");
                }
                phiPreds.SymmetricExceptWith(phi.Block.Preds.AsEnumerable());
                Check(phiPreds.Count == 0, phi, "Phi must have one argument for each block predecessor");
                break;
            }
            case StoreVarInst { Var.ResultType: var dstType, Value.ResultType: var srcType }: {
                if (!srcType.IsStackAssignableTo(dstType)) {
                    Error(inst, $"Store to incompatible type: {srcType} -> {dstType}", DiagnosticSeverity.Warn);
                }
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