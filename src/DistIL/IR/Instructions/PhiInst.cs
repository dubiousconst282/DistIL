namespace DistIL.IR;

/// <summary>
/// The Phi instruction maps the incomming predecessor block into the value of the argument with the same block.
/// Arguments should not have duplicated blocks.
/// </summary>
public class PhiInst : Instruction
{
    public int NumArgs => Operands.Length / 2;
    public override string InstName => "phi";
    public override bool IsHeader => true;

    public PhiInst(TypeDesc type)
    {
        ResultType = type;
    }
    public PhiInst(params PhiArg[] args)
        : base(InterleaveArgs(args))
    {
        ResultType = args[0].Value.ResultType;
        Assert(args.All(a => a.Value.ResultType.IsStackAssignableTo(ResultType)));
    }
    
    /// <summary> Unchecked non-copying constructor. </summary>
    /// <param name="operands">
    /// Operand array containing pairs of [PredBlock, IncommingValue].
    /// The instruction will take ownership of this array, its elements should not be modified after.
    /// </param>
    public PhiInst(TypeDesc resultType, Value[] operands)
        : base(operands)
    {
        ResultType = resultType;
    }
    
    public BasicBlock GetBlock(int index) => (BasicBlock)Operands[index * 2 + 0];
    public Value GetValue(int index) => Operands[index * 2 + 1];

    /// <summary> Returns the incomming value for the given predecessor block. If it doesn't exist, an exception is thrown. </summary>
    public Value GetValue(BasicBlock block) => GetValue(FindArgIndex(block));

    /// <summary> Sets the incomming value for the given predecessor block. If it doesn't exist, an exception is thrown. </summary>
    public void SetValue(BasicBlock block, Value newValue)
    {
        int index = FindArgIndex(block);
        ReplaceOperand(index * 2 + 1, newValue);
    }
    public void SetValue(int index, Value newValue)
    {
        ReplaceOperand(index * 2 + 1, newValue);
    }

    public PhiArg GetArg(int index)
    {
        var block = Operands[index * 2 + 0];
        var value = Operands[index * 2 + 1];
        return new PhiArg((BasicBlock)block, value);
    }

    public void AddArg(BasicBlock block, Value value)
    {
        int index = GrowOperands(2);
        ReplaceOperand(index + 0, block);
        ReplaceOperand(index + 1, value);
    }
    public void AddArg(params PhiArg[] args)
    {
        int index = GrowOperands(args.Length * 2);

        foreach (var (block, value) in args) {
            ReplaceOperand(index + 0, block);
            ReplaceOperand(index + 1, value);
            index += 2;
        }
    }

    public void RemoveArg(int index, bool removeTrivialPhi)
    {
        Ensure(index >= 0 && index < NumArgs);
        if (removeTrivialPhi && NumArgs == 2) {
            ReplaceWith(GetValue(1 - index), false);
            return;
        }
        RemoveOperands(index * 2, 2);
    }

    public void RemoveArg(BasicBlock block, bool removeTrivialPhi) 
        => RemoveArg(FindArgIndex(block), removeTrivialPhi);

    private int FindArgIndex(BasicBlock block)
    {
        for (int i = 0; i < Operands.Length; i += 2) {
            if (Operands[i] == block) {
                return i / 2;
            }
        }
        throw new KeyNotFoundException("Phi doesn't have a mapping for the specified block");
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(PrintContext ctx)
    {
        for (int i = 0; i < NumArgs; i++) {
            var (block, value) = GetArg(i);

            ctx.Print(i == 0 ? " [" : ", [");
            block.PrintAsOperand(ctx);
            ctx.Print(" -> ");
            value.PrintAsOperand(ctx);
            ctx.Print("]");
        }
    }

    public IEnumerator<PhiArg> GetEnumerator()
    {
        for (int i = 0; i < NumArgs; i++){
            yield return GetArg(i);
        }
    }
    
    private static Value[] InterleaveArgs(PhiArg[] args)
    {
        var opers = new Value[args.Length * 2];
        for (int i = 0; i < args.Length; i++) {
            opers[i * 2 + 0] = args[i].Block;
            opers[i * 2 + 1] = args[i].Value;
        }
        return opers;
    }
}
//typedef PhiArg = (BasicBlock block, Value value);
public struct PhiArg
{
    public BasicBlock Block;
    public Value Value;

    public PhiArg(BasicBlock block, Value value)
        => (Block, Value) = (block, value);

    public static implicit operator PhiArg(ValueTuple<BasicBlock, Value> tuple)
        => new(tuple.Item1, tuple.Item2);

    public void Deconstruct(out BasicBlock block, out Value value)
        => (block, value) = (Block, Value);
}