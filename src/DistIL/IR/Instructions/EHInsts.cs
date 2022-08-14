namespace DistIL.IR;

using System.Text;

/// <summary>
/// Starts a protected region (try block) on the parent block.
/// The result of this instruction is the thrown exception object, which is undefined outside handler/filter blocks.
/// </summary>
public class GuardInst : Instruction
{
    /// <summary> The first handler block. </summary>
    public BasicBlock HandlerBlock {
        get => (BasicBlock)Operands[0];
        set => ReplaceOperand(0, value);
    }
    /// <summary> The first filter block. </summary>
    public BasicBlock? FilterBlock {
        get => HasFilter ? (BasicBlock)Operands[1] : null;
        set {
            Ensure(value != null && HasFilter);
            ReplaceOperand(1, value);
        }
    }
    [MemberNotNullWhen(true, nameof(FilterBlock))]
    public bool HasFilter => Kind == GuardKind.Catch && Operands.Length >= 2;

    public TypeDesc? CatchType => ResultType;
    public GuardKind Kind { get; set; }

    public override string InstName => "try";
    public override bool SafeToRemove => false;
    public override bool IsHeader => true;

    public GuardInst(GuardKind kind, BasicBlock handlerBlock, TypeDesc? catchType = null, BasicBlock? filterBlock = null)
        : base(filterBlock == null ? new Value[] { handlerBlock } : new Value[] { handlerBlock, filterBlock })
    {
        Kind = kind;
        ResultType = catchType ?? PrimType.Object;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(PrintContext ctx)
    {
        ctx.Print($" {Kind.ToString().ToLower()}", PrintToner.InstName);
        ctx.Print(" -> ");
        HandlerBlock.Print(ctx);

        if (HasFilter) {
            ctx.Print(", ");
            ctx.Print("filter", PrintToner.InstName);
            ctx.Print(" -> ");
            FilterBlock.Print(ctx);
        }
    }
}
public enum GuardKind
{
    Catch, Fault, Finally
}

/// <summary> Leaves a protected region. </summary>
public class LeaveInst : Instruction
{
    public GuardInst ParentGuard {
        get => (GuardInst)Operands[0];
        set => ReplaceOperand(0, value);
    }
    public BasicBlock Target {
        get => (BasicBlock)Operands[1];
        set => ReplaceOperand(1, value);
    }

    public override string InstName => "leave";
    public override bool IsBranch => true;

    public LeaveInst(GuardInst parentGuard, BasicBlock target)
        : base(parentGuard, target)
    {
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
/// <summary> Leaves a filter/finally region. </summary>
public class ContinueInst : Instruction
{
    public GuardInst ParentGuard {
        get => (GuardInst)Operands[0];
        set => ReplaceOperand(0, value);
    }
    public Value? FilterResult {
        get => IsFromFilter ? Operands[1] : null;
        set {
            Ensure(value != null && IsFromFilter);
            ReplaceOperand(1, value);
        }
    }
    [MemberNotNullWhen(true, nameof(FilterResult))]
    public bool IsFromFilter => Operands.Length >= 2;

    public override string InstName => "continue";
    public override bool IsBranch => true;

    public ContinueInst(GuardInst parentGuard)
        : base(parentGuard)
    {
    }
    public ContinueInst(GuardInst parentGuard, Value? filterResult = null)
        : base(filterResult == null ? new Value[] { parentGuard } : new Value[] { parentGuard, filterResult })
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
            Ensure(value != null && Operands.Length > 0);
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