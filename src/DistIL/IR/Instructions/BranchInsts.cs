namespace DistIL.IR;

public class BranchInst : Instruction
{
    public Value? Cond {
        get => IsConditional ? Operands[0] : null;
        set {
            Ensure.That(value != null && IsConditional, "Branch condition cannot be added or removed after construction");
            ReplaceOperand(0, value);
        }
    }
    public BasicBlock Then {
        get => (BasicBlock)Operands[IsConditional ? 1 : 0];
        set => ReplaceOperand(IsConditional ? 1 : 0, value);
    }
    public BasicBlock? Else {
        get => IsConditional ? (BasicBlock)Operands[2] : null;
        set {
            Ensure.That(value != null && IsConditional, "Branch else target cannot be added or removed after construction");
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
        : base(target) { }

    public BranchInst(Value cond, BasicBlock then, BasicBlock else_)
        // Implicitly fold conditional branches with the same target, to guarantee
        // that branch block uses are unique (as required by BasicBlock edge iterators).
        : base(then == else_ ? [then] : [cond, then, else_]) { }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    public override void Print(PrintContext ctx)
    {
        ctx.Print("goto ", PrintToner.InstName);

        if (IsConditional) {
            ctx.Print($"{Cond} ? {Then} : {Else}");
        } else {
            ctx.PrintAsOperand(Then);
        }
    }
}

public class SwitchInst : Instruction
{
    public Value TargetIndex {
        get => Operands[0];
        set => ReplaceOperand(0, value);
    }
    public BasicBlock DefaultTarget {
        get => GetTarget(-1);
    }
    /// <summary> Maps a case index (<see cref="TargetIndex"/>) into a block index at <see cref="Instruction.Operands"/>. </summary>
    /// <remarks> Target indices are offset by 1 in this array, the 0th element represents the default case. </remarks>
    public int[] TargetMappings { get; }

    public int NumTargets => TargetMappings.Length - 1;

    public override string InstName => "switch";
    public override bool IsBranch => true;

    public SwitchInst(Value targetIndex, BasicBlock defaultTarget, params BasicBlock[] targets)
        : base(CreateOperands(targetIndex, defaultTarget, targets, out int[] mappings))
    {
        TargetMappings = mappings;
    }
    /// <summary> Unchecked constructor. </summary>
    public SwitchInst(Value[] operands, int[] targetMappings)
        : base(operands)
    {
        Debug.Assert(operands.Distinct().Count() == operands.Length);
        TargetMappings = targetMappings;
    }

    // Branch instructions are required by the successor edge iterator to have no duplicate block uses.
    private static Value[] CreateOperands(Value targetIndex, BasicBlock defaultTarget, BasicBlock[] targets, out int[] mappings)
    {
        var blockMappings = new Dictionary<BasicBlock, int>(targets.Length + 1);
        mappings = new int[targets.Length + 1];
        var opers = new Value[targets.Length + 2];
        int operIdx = 0;
        opers[operIdx++] = targetIndex;

        for (int i = -1; i < targets.Length; i++) {
            var target = i >= 0 ? targets[i] : defaultTarget;
            if (!blockMappings.TryGetValue(target, out int mappingIdx)) {
                blockMappings[target] = mappingIdx = operIdx;
                opers[operIdx++] = target;
            }
            mappings[i + 1] = mappingIdx;
        }
        return operIdx == opers.Length ? opers : opers[0..operIdx]; // slicing always creates a copy
    }

    /// <summary> Returns the target block for the case at <paramref name="index"/>. If the index is out of range, returns the default case target. </summary>
    public BasicBlock GetTarget(int index)
    {
        if (index < 0 || index >= NumTargets) {
            index = -1;
        }
        return (BasicBlock)_operands[TargetMappings[index + 1]];
    }

    /// <summary> Returns all unique target blocks in this switch. </summary>
    public IEnumerable<BasicBlock> GetUniqueTargets()
    {
        for (int i = 1; i < _operands.Length; i++) {
            yield return (BasicBlock)_operands[i];
        }
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    public override void Print(PrintContext ctx)
    {
        ctx.Print("switch ", PrintToner.InstName);
        ctx.PrintAsOperand(TargetIndex);
        ctx.Push(", [");

        ctx.Print("_: ");
        ctx.PrintAsOperand(DefaultTarget);
        
        for (int i = 0; i < NumTargets; i++) {
            ctx.PrintLine(",");
            ctx.Print(i.ToString(), PrintToner.Number);
            ctx.Print(": ");
            ctx.PrintAsOperand(GetTarget(i));
        }
        ctx.Pop("]");
    }
}

public class ReturnInst : Instruction
{
    public Value? Value {
        get => HasValue ? Operands[0] : null;
        set {
            Ensure.That(Operands.Length > 0 && value != null, "Cannot add return value after construction.");
            ReplaceOperand(0, value);
        }
    }
    [MemberNotNullWhen(true, nameof(Value))]
    public bool HasValue => Operands.Length > 0;

    public override string InstName => "ret";
    public override bool IsBranch => true;

    public ReturnInst(Value? value = null)
        : base(value == null ? [] : [value]) { }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}