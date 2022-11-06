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

    /// <summary> Verifies the method body and returns a list of diagnostics. </summary>
    public static List<Diagnostic> Diagnose(MethodBody method)
    {
        var v = new IRVerifier(method);
        v.VerifyEdges();
        v.VerifyPhis();
        v.VerifyUses();
        return v._diags;
    }

    private void VerifyEdges()
    {
        if (_method.EntryBlock.NumPreds != 0) {
            Error(_method.EntryBlock, "Entry block should not have predecessors");
        }
        foreach (var block in _method) {
            if (!block.Last.IsBranch) {
                Error(block, "Block must end with a valid terminator");
            }
        }
    }

    private void VerifyPhis()
    {
        var phiPreds = new HashSet<BasicBlock>();

        foreach (var block in _method) {
            foreach (var phi in block.Phis()) {
                foreach (var arg in phi) {
                    if (!phiPreds.Add(arg.Block)) {
                        Error(phi, "Phi should not have duplicated block arguments");
                    }
                }
                phiPreds.SymmetricExceptWith(block.Preds.AsEnumerable());
                if (phiPreds.Count != 0) {
                    Error(phi, "Phi must have one argument for each block predecessor");
                }
                phiPreds.Clear();
            }
        }
    }

    private void VerifyUses()
    {
        var values = new Dictionary<TrackedValue, HashSet<Instruction>>();
        var blockIndices = new Dictionary<TrackedValue, int>();
        var domTree = new DominatorTree(_method);

        foreach (var block in _method) {
            int index = 0;
            foreach (var inst in block) {
                foreach (var oper in inst.Operands) {
                    if (oper is TrackedValue trackedOper) {
                        var expUsers = values.GetOrAddRef(trackedOper) ??= new();
                        expUsers.Add(inst);
                    }
                }
                blockIndices[inst] = index++;
            }
        }
        foreach (var (val, expUsers) in values) {
            //Check use list correctness
            var actUsers = new HashSet<Instruction>();
            foreach (var user in val.Users()) {
                actUsers.Add(user);
            }
            actUsers.SymmetricExceptWith(expUsers);
            if (actUsers.Count != 0) {
                Error(val, "Invalid value user set");
            }
            //Check dominance
            if (val is Instruction defInst) {
                foreach (var user in defInst.Users()) {
                    if (user is Instruction userInst && !IsDominatedByDef(defInst, userInst)) {
                        Error(user, "Using non-dominating instruction");
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
            if (user.Block == def.Block && blockIndices[def] >= blockIndices[user]) {
                return false;
            }
            return domTree.Dominates(def.Block, user.Block);
        }
    }

    private static bool AreSetsEqual<T>(IEnumerable<T> list1, IEnumerable<T> list2, IEqualityComparer<T>? comparer = null)
    {
        var set = new HashSet<T>(list1, comparer);
        set.SymmetricExceptWith(list2);
        return set.Count == 0;
    }

    private bool Error(Value location, string msg)
    {
        _diags.Add(new Diagnostic() {
            Kind = DiagnosticKind.Error,
            Location = location,
            Message = msg
        });
        return true;
    }
}

public struct Diagnostic
{
    public DiagnosticKind Kind { get; init; }
    /// <summary> The BasicBlock or Instruction originating this diagnostic. </summary>
    public Value Location { get; init; }
    public string? Message { get; init; }

    public override string ToString()
    {
        return $"[{Kind}] {Message} ({Location})";
    }
}
public enum DiagnosticKind
{
    Warn,
    Error,
}