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
        v.VerifyMethod();
        return v._diags;
    }

    private void VerifyMethod()
    {
        Check(_method.EntryBlock.NumPreds == 0, _method.EntryBlock, "Entry block should not have predecessors");

        var useChecker = new UseChecker();
        var regionAnalysis = new ProtectedRegionAnalysis(_method);

        foreach (var block in _method) {
            int blockInstIdx = 0;

            foreach (var inst in block) {
                CheckInst(inst);
                useChecker.AddInst(this, inst, blockInstIdx++);
            }
            CheckTerminator(block, regionAnalysis);
        }
        useChecker.Validate(this, regionAnalysis);
    }

    private void CheckInst(Instruction inst)
    {
        switch (inst) {
            case GuardInst guard: {
                Check(
                    guard.HandlerBlock.NumPreds == 1 && (!guard.HasFilter || guard.FilterBlock.NumPreds == 1), guard,
                    "Guard handler/filter block must have a single predecessor");
                Check(
                    guard.HandlerBlock.First is not GuardInst && (!guard.HasFilter || guard.FilterBlock.First is not GuardInst), guard,
                    "Handler/filter entry block should not have guards");
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
                bool argTypesMatch = true;

                foreach (var (pred, val) in phi) {
                    Check(preds.Add(pred), phi, "Phi should not have duplicated block arguments");

                    var type = phi.ResultType;
                    argTypesMatch &= val switch {
                        ConstNull => type.StackType == StackType.Object,
                        ConstInt ci when type.Kind.IsSmallInt() => ci.FitsInType(type),
                        _ => val.ResultType.IsAssignableTo(type)
                    };
                }
                preds.SymmetricExceptWith(phi.Block.Preds.AsEnumerable());
                Check(preds.Count == 0, phi, "Phi must have one argument for each block predecessor");
                Check(argTypesMatch, phi, "Phi arguments should be assignable to its result type", DiagnosticSeverity.Warn);
                break;
            }
            case { IsBranch: true, Next: not null }: {
                Error(inst, "Branch must be the last instruction in the block");
                break;
            }
        }
    }

    private void CheckTerminator(BasicBlock block, ProtectedRegionAnalysis regionAnalysis)
    {
        switch (block.Last) {
            case BranchInst or SwitchInst: {
                var currRegion = regionAnalysis.GetBlockRegion(block);

                foreach (var oper in block.Last.Operands) {
                    if (oper is not BasicBlock succ) continue;

                    var succRegion = regionAnalysis.GetBlockRegion(succ);

                    if (currRegion != succRegion && !(succ == succRegion.StartBlock && succRegion.Parent == currRegion)) {
                        Error(block, $"Branch target must be within the same region");
                        break;
                    }
                }
                break;
            }
            case LeaveInst or ResumeInst: {
                var region = regionAnalysis.GetBlockRegion(block);

                if (region == regionAnalysis.Root) {
                    Error(block, "Cannot leave or resume from root region");
                } else if (block.Last is ResumeInst resume) {
                    var guard = region.GetHandlerGuard();

                    if (guard == null) {
                        Error(block, "Missing guard for filter or finally region");
                        break;
                    }

                    bool isValid = 
                        resume.IsFromFilter
                            ? guard.FilterBlock == region.StartBlock
                            : guard.Kind is GuardKind.Finally or GuardKind.Fault;
                    Check(isValid, block, "ResumeInst should not be used outside filter, finally, or fault handlers");

                    var protectedRegion = regionAnalysis.GetBlockRegion(guard.Block);
                    var succBlocks = new HashSet<BasicBlock>();
                    
                    foreach (var exitBlock in protectedRegion.GetExitBlocks()) {
                        Debug.Assert(exitBlock.Last is LeaveInst);
                        succBlocks.Add(exitBlock.Succs.First());
                    }
                    succBlocks.SymmetricExceptWith(resume.GetExitTargets());

                    Check(succBlocks.Count == 0, resume, "ResumeInst targets must be consistent with exits in sibling protected region");
                }
                break;
            }
            case ReturnInst: {
                if (regionAnalysis.GetBlockRegion(block) != regionAnalysis.Root) {
                    Error(block, $"ReturnInst should not be used inside a protected region");
                }
                break;
            }
            default: {
                Check(block.Last.IsBranch, block, "Block must end with a valid terminator");
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
            }
            _blockIndices[inst] = blockIndex;
        }

        public void Validate(IRVerifier verifier, ProtectedRegionAnalysis regionAnalysis)
        {
            var domTree = new DominatorTree(verifier._method);
            var actUsers = new HashSet<Instruction>();

            foreach (var (val, expUsers) in _expectedUsers) {
                // Check use list correctness
                actUsers.UnionWith(val.Users().AsEnumerable());
                actUsers.SymmetricExceptWith(expUsers);
                verifier.Check(actUsers.Count == 0, val, "Invalid value use chain");
                actUsers.Clear();

                // Check users
                if (val is Instruction def) {
                    var defRegion = regionAnalysis.GetBlockRegion(def.Block);
                    
                    foreach (var user in def.Users()) {
                        if (!IsDominatedByDef(domTree, def, user)) {
                            verifier.Error(user, $"Using non-dominating instruction '{def}'");
                        }
                        if (def is not GuardInst && def.Block != user.Block && 
                            defRegion.StartBlock.Guards().Any(g => g.Kind != GuardKind.Finally) && 
                            defRegion.FindInnermostParent(user.Block) == null
                        ) {
                            verifier.Error(user, $"Using instruction defined inside a child region '{def}'", DiagnosticSeverity.Warn);
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
        return $"[{Severity}] {Message} (at {Location})";
    }
}
public enum DiagnosticSeverity
{
    Info,
    Warn,
    Error,
}