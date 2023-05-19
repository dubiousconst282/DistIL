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
        : base(filterBlock == null ? new Value[] { handlerBlock } : new Value[] { handlerBlock, filterBlock })
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
        : base(target)
    {
    }

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
    public bool IsFromFilter => Operands.Length > 0;

    public override string InstName => "resume";
    public override bool IsBranch => true;

    public ResumeInst(Value? filterResult = null)
        : base(filterResult == null ? Array.Empty<Value>() : new Value[] { filterResult })
    {
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
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
        : base(exception == null ? Array.Empty<Value>() : new Value[] { exception })
    {
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}