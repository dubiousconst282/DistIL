namespace DistIL.IR;

/// <summary>
/// Starts a protected region (try block) on the parent block.
/// The result of this instruction is the thrown exception object, which is undefined outside handler/filter blocks.
/// </summary>
public class GuardInst : Instruction
{
    /// <summary> The handler entry block. </summary>
    public BasicBlock HandlerBlock {
        get => (BasicBlock)Operands[0];
        set => ReplaceOperand(0, value);
    }
    /// <summary> The filter entry block. </summary>
    public BasicBlock? FilterBlock {
        get => HasFilter ? (BasicBlock)Operands[1] : null;
        set {
            Ensure.That(value != null && HasFilter);
            ReplaceOperand(1, value);
        }
    }
    [MemberNotNullWhen(true, nameof(FilterBlock))]
    public bool HasFilter => Kind == GuardKind.Catch && Operands.Length >= 2;

    public TypeDesc? CatchType => ResultType;
    public GuardKind Kind { get; set; }

    public override string InstName => "try";
    public override bool SafeToRemove => false;

    public GuardInst(GuardKind kind, BasicBlock handlerBlock, TypeDesc? catchType = null, BasicBlock? filterBlock = null)
        : base(filterBlock == null ? [handlerBlock] : [handlerBlock, filterBlock])
    {
        Kind = kind;
        ResultType = catchType ?? (HasFilter ? PrimType.Object : PrimType.Void);
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(PrintContext ctx)
    {
        ctx.Print($" {PrintToner.InstName}{Kind.ToString().ToLower()}({HandlerBlock})");

        if (HasFilter) {
            ctx.Print($" {PrintToner.InstName}filter{PrintToner.Default}({FilterBlock})");
        }
    }
}
public enum GuardKind
{
    Catch, Fault, Finally
}

/// <summary> Leaves the current protected region. </summary>
public class LeaveInst : Instruction
{
    public BasicBlock Target {
        get => (BasicBlock)Operands[0];
        set => ReplaceOperand(0, value);
    }

    public override string InstName => "leave";
    public override bool IsBranch => true;

    public LeaveInst(BasicBlock target)
        : base(target) { }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
/// <summary> Resumes control flow from a filter/finally region. </summary>
public class ResumeInst : Instruction
{
    public Value? FilterResult {
        get => IsFromFilter ? Operands[0] : null;
        set {
            Ensure.That(value != null && IsFromFilter);
            ReplaceOperand(0, value);
        }
    }

    [MemberNotNullWhen(true, nameof(FilterResult))]
    public bool IsFromFilter => Operands is [Value and not BasicBlock, ..];

    public override string InstName => "resume";
    public override bool IsBranch => true;

    public ResumeInst(IEnumerable<BasicBlock> exitTargets, Value? filterResult = null)
        : base(filterResult == null ? exitTargets.Distinct().ToArray() : [filterResult, ..exitTargets]) { }

    /// <summary> Unchecked cloning constructor. </summary>
    public ResumeInst(int _, Value[] operands)
        : base(operands) { }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    public void SetExitTargets(IReadOnlySet<BasicBlock> targets)
    {
        int startIdx = IsFromFilter ? 1 : 0;
        int newCount = targets.Count + startIdx;

        if (newCount > _operands.Length) {
            startIdx = GrowOperands(newCount - _operands.Length);
        } else if (newCount < _operands.Length) {
            RemoveOperands(newCount, _operands.Length - newCount);
        }

        foreach (var target in targets) {
            _operands[startIdx] = target;
            target.AddUse(this, startIdx);
            startIdx++;
        }
    }

    public IEnumerable<BasicBlock> GetExitTargets() => _operands.Skip(IsFromFilter ? 1 : 0).Cast<BasicBlock>();
}

/// <summary> Throws or rethrows an exception. </summary>
public class ThrowInst : Instruction
{
    public Value? Exception {
        get => Operands.Length > 0 ? Operands[0] : null;
        set {
            Ensure.That(value != null && Operands.Length > 0);
            ReplaceOperand(0, value);
        }
    }
    [MemberNotNullWhen(false, nameof(Exception))]
    public bool IsRethrow => Operands.Length == 0;

    public override string InstName => IsRethrow ? "rethrow" : "throw";
    public override bool MayThrow => true;
    public override bool IsBranch => true;

    public ThrowInst(Value? exception = null)
        : base(exception == null ? [] : [exception]) { }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}