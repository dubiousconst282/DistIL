namespace DistIL.IR.Utils;

public class IRVerifier
{
    readonly Method _method;
    readonly List<Diagnostic> _diags = new();

    public IRVerifier(Method method)
    {
        _method = method;
    }

    /// <summary> Verifies the method body and returns a list of diagnostics. </summary>
    public List<Diagnostic> GetDiagnostics()
    {
        VerifyEdges();
        VerifyPhis();
        return _diags;
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
                Error(block, "Unexpected successor edge");
            }
            foreach (var succ in succs) {
                var preds = expPreds.GetOrAddRef(succ) ??= new();
                preds.Add(block);
            }
        }
        //Verify preds
        foreach (var (block, preds) in expPreds) {
            if (!AreSetsEqual(preds, block.Preds)) {
                Error(block, "Unexpected predecessor edge");
            }
        }

        BasicBlock[] CalculateSuccs(BasicBlock block)
        {
            return block.Last switch {
                BranchInst br => br.IsConditional ? new[] { br.Then, br.Else } : new[] { br.Then },
                SwitchInst sw => sw.GetTargets().ToArray(),
                ReturnInst rt => new BasicBlock[0],
                _ => (new BasicBlock[0], Error(block, "Invalid block terminator")).Item1
            };
        }
    }

    private void VerifyPhis()
    {
        var phiPreds = new HashSet<BasicBlock>();

        foreach (var block in _method) {
            foreach (var phi in block.Phis()) {
                foreach (var arg in phi) {
                    phiPreds.Add(arg.Block);
                }
                phiPreds.SymmetricExceptWith(block.Preds);
                if (phiPreds.Count != 0) {
                    Error(phi, "Phi must have one argument for each predecessor");
                }
                phiPreds.Clear();
            }
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
}
public enum DiagnosticKind
{
    Error
}