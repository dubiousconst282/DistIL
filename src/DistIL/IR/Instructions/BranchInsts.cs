namespace DistIL.IR;

public class BranchInst : Instruction
{
    public Value? Cond {
        get => IsConditional ? Operands[0] : null;
        set {
            Ensure(value != null && IsConditional, "Branch condition cannot be added or removed after construction");
            ReplaceOperand(0, value);
        }
    }
    public BasicBlock Then {
        get => (BasicBlock)(Operands[IsConditional ? 1 : 0]);
        set => ReplaceOperand(IsConditional ? 1 : 0, value);
    }
    public BasicBlock? Else {
        get => IsConditional ? (BasicBlock)Operands[2] : null;
        set {
            Ensure(value != null && IsConditional, "Branch else target cannot be added or removed after construction");
            ReplaceOperand(2, value);
        }
    }
    [MemberNotNullWhen(true, nameof(Cond), nameof(Else))]
    public bool IsConditional => Operands.Length > 1;

    [MemberNotNullWhen(false, nameof(Cond), nameof(Else))]
    public bool IsJump => Operands.Length == 1;

    public override string InstName => "br";
    public override bool IsBranch => true;

    public BranchInst(BasicBlock target)
        : base(target)
    {
    }
    public BranchInst(Value cond, BasicBlock then, BasicBlock else_)
        : base(cond, then, else_)
    {
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    public override void Print(PrintContext ctx)
    {
        ctx.Print("goto ", PrintToner.InstName);

        if (IsConditional) {
            Cond.PrintAsOperand(ctx);
            ctx.Print(" ? ");
            Then.PrintAsOperand(ctx);
            ctx.Print(" : ");
            Else.PrintAsOperand(ctx);
        } else {
            Then.PrintAsOperand(ctx);
        }
    }
}

/// <summary>
/// Represents a switch instruction:
/// <code>goto (uint)Value &lt; NumTargets ? Targets[Value] : DefaultTarget;</code>
/// </summary>
public class SwitchInst : Instruction
{
    public Value Value {
        get => Operands[0];
        set => ReplaceOperand(0, value);
    }
    public BasicBlock DefaultTarget {
        get => (BasicBlock)Operands[1];
        set => ReplaceOperand(1, value);
    }
    public int NumTargets => Operands.Length - 2;

    public override string InstName => "switch";
    public override bool IsBranch => true;

    public SwitchInst(Value value, BasicBlock defaultTarget, params BasicBlock[] targets)
        : base(targets.Prepend(defaultTarget).Prepend(value).ToArray())
    {
    }
    /// <summary> Unchecked non-copying constructor. </summary>
    /// <param name="operands">
    /// Operand array containing [Value, DefaultTarget, Targets...].
    /// The instruction will take ownership of this array, its elements should not be modified after.
    /// </param>
    public SwitchInst(Value[] operands)
        : base(operands)
    {
    }

    public BasicBlock GetTarget(int index) => (BasicBlock)Operands[index + 2];
    public void SetTarget(int index, BasicBlock block) => ReplaceOperand(index + 2, block);

    /// <summary> Returns all target blocks in this switch, including the default (first element). </summary>
    public IEnumerable<BasicBlock> GetTargets()
    {
        for (int i = 1; i < Operands.Length; i++) {
            yield return (BasicBlock)Operands[i];
        }
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    public override void Print(PrintContext ctx)
    {
        ctx.Print("switch ", PrintToner.InstName);
        Value.PrintAsOperand(ctx);
        ctx.Push(", [");

        ctx.Print("_: ");
        DefaultTarget.PrintAsOperand(ctx);
        
        for (int i = 0; i < NumTargets; i++) {
            ctx.PrintLine(",");
            ctx.Print(i + ": ");
            GetTarget(i).PrintAsOperand(ctx);
        }
        ctx.Pop("]");
    }
}

public class ReturnInst : Instruction
{
    public Value? Value {
        get => HasValue ? Operands[0] : null;
        set {
            Ensure(Operands.Length > 0 && value != null, "Cannot add return value after construction.");
            ReplaceOperand(0, value);
        }
    }
    [MemberNotNullWhen(true, nameof(Value))]
    public bool HasValue => Operands.Length > 0;

    public override string InstName => "ret";
    public override bool IsBranch => true;

    public ReturnInst(Value? value = null)
        : base(value == null ? Array.Empty<Value>() : new[] { value })
    {
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}