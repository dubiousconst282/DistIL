namespace DistIL.IR.Utils;

using DistIL.Analysis;

public class Verifier
{
    readonly MethodBody _method;
    readonly List<Diagnostic> _diags = new();

    private Verifier(MethodBody method)
    {
        _method = method;
    }

    /// <summary> Verifies the method body and returns a list of diagnostics. </summary>
    public static List<Diagnostic> Diagnose(MethodBody method)
    {
        var v = new Verifier(method);
        v.VerifyEdges();
        v.VerifyPhis();
        v.VerifyUses();
        return v._diags;
    }

    private void VerifyEdges()
    {
        if (_method.EntryBlock.Preds.Count > 0) {
            Error(_method.EntryBlock, "Entry block should not have predecessors");
        }
        var expPreds = new Dictionary<BasicBlock, List<BasicBlock>>();
        //Verify successors and build expected preds
        foreach (var block in _method) {
            var succs = CalculateSuccs(block);

            if (!AreSetsEqual(succs, block.Succs)) {
                Error(block, "Invalid successor list");
            }
            foreach (var succ in succs) {
                var preds = expPreds.GetOrAddRef(succ) ??= new();
                preds.Add(block);
            }
        }
        //Verify preds
        foreach (var (block, preds) in expPreds) {
            if (!AreSetsEqual(preds, block.Preds)) {
                Error(block, "Invalid predecessor list");
            }
        }

        List<BasicBlock> CalculateSuccs(BasicBlock block)
        {
            var succs = new List<BasicBlock>();
            foreach (var guard in block.Guards()) {
                succs.Add(guard.HandlerBlock);
                if (guard.HasFilter) {
                    succs.Add(guard.FilterBlock);
                }
            }

            switch (block.Last) {
                case BranchInst br: {
                    succs.Add(br.Then);
                    if (br.IsConditional) {
                        succs.Add(br.Else);
                    }
                    break;
                }
                case SwitchInst sw: {
                    succs.AddRange(sw.GetTargets());
                    break;
                }
                case LeaveInst lv: {
                    succs.Add(lv.Target);
                    break;
                }
                case ReturnInst or ContinueInst: break;
                default:
                    Error(block, "Invalid block terminator");
                    break;
            }
            return succs;
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
                phiPreds.SymmetricExceptWith(block.Preds);
                if (phiPreds.Count != 0) {
                    Error(phi, "Phi must have one argument for each predecessor in the parent block");
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

    private static bool AreSetsEqual<T>(ICollection<T> list1, ICollection<T> list2, IEqualityComparer<T>? comparer = null)
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