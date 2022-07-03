namespace DistIL.IR;

/// <summary>
/// Represents the SSA Phi instruction. The result is one of the arguments, selected based on the incomming predecessor block.
/// Arguments are interleaved in `Operands`, e.g. `[block1, value1, block2, value2, ...]`.
/// Use GetArg() and NumArgs to access them.
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
        Assert(args.All(a => a.Value.ResultType == ResultType));
    }
    
    public PhiArg GetArg(int index)
    {
        var block = Operands[index * 2 + 0];
        var value = Operands[index * 2 + 1];
        return ((BasicBlock)block, value);
    }
    public BasicBlock GetBlock(int index) => (BasicBlock)Operands[index * 2 + 0];
    public Value GetValue(int index) => Operands[index * 2 + 1];

    /// <summary> Returns the value for the given incomming block. If it doesn't exist, an exception is thrown. </summary>
    public Value GetValue(BasicBlock block) => GetValue(FindArgIndex(block));

    public void SetArg(int index, BasicBlock block, Value value)
    {
        Ensure(index >= 0 && index < NumArgs);
        ReplaceOperand(index * 2 + 0, block);
        ReplaceOperand(index * 2 + 1, block);
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
        if (removeTrivialPhi && NumArgs == 2) {
            ReplaceWith(GetValue(1 - index), false);
            return;
        }
        Ensure(index >= 0 && index < NumArgs);
        RemoveOperands(index * 2, 2);
    }

    public void RemoveArg(BasicBlock block, bool removeTrivialPhi) 
        => RemoveArg(FindArgIndex(block), removeTrivialPhi);

    private int FindArgIndex(BasicBlock block)
    {
        for (int i = 0; i < NumArgs; i++) {
            if (GetBlock(i) == block) {
                return i;
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