namespace DistIL.IR;

/// <summary>
/// The Phi instruction maps the incomming predecessor block in the CFG execution path to one of its corresponding arguments.
/// There should not be duplicated blocks in the arguments.
/// </summary>
public class PhiInst : Instruction
{
    public int NumArgs => _operands.Length / 2;
    public override string InstName => "phi";
    public override bool IsHeader => true;

    public PhiInst(TypeDesc type)
    {
        ResultType = type;
    }
    public PhiInst(TypeDesc type, params PhiArg[] args)
        : base(InterleaveArgs(args))
    {
        ResultType = type;
    }
    
    /// <summary> Unchecked non-copying constructor. </summary>
    /// <param name="operands">
    /// Operand array containing pairs of [PredBlock, IncommingValue].
    /// The instruction will take ownership of this array, its elements should not be modified after.
    /// </param>
    internal PhiInst(TypeDesc resultType, Value[] operands)
        : base(operands)
    {
        ResultType = resultType;
    }
    
    /// <summary> Returns the incomming value for the given predecessor block. If it doesn't exist, an exception is thrown. </summary>
    public Value GetValue(BasicBlock block) => GetValue(FindArgIndex(block));
    public Value GetValue(int index) => _operands[index * 2 + 1];

    /// <summary> Sets the incomming value for the given predecessor block. If it doesn't exist, an exception is thrown. </summary>
    public void SetValue(BasicBlock block, Value newValue) => SetValue(FindArgIndex(block), newValue);
    public void SetValue(int index, Value newValue) => ReplaceOperand(index * 2 + 1, newValue);

    /// <summary> Returns the first block which maps to `value`. If it doesn't exist, an exception is thrown. </summary>
    public BasicBlock GetBlock(Value value) => GetBlock(FindArgIndex(value, true));
    public BasicBlock GetBlock(int index) => (BasicBlock)_operands[index * 2 + 0];

    public PhiArg GetArg(int index) => new(GetBlock(index), GetValue(index));

    public void AddArg(BasicBlock block, Value value)
    {
        int index = GrowOperands(2);
        InsertArg(index, block, value);
    }

    public void AddArg(params PhiArg[] args)
    {
        int index = GrowOperands(args.Length * 2);

        foreach (var (block, value) in args) {
            InsertArg(index, block, value);
            index += 2;
        }
    }

    private void InsertArg(int index, BasicBlock block, Value value)
    {
        Debug.Assert(_operands[index] == null);
        ReplaceOperand(index + 0, block);
        ReplaceOperand(index + 1, value);
    }

    public void RemoveArg(int index, bool removeTrivialPhi)
    {
        Ensure.IndexValid(index, NumArgs);

        if (removeTrivialPhi && NumArgs == 2) {
            ReplaceWith(GetValue(1 - index));
            return;
        }
        RemoveOperands(index * 2, 2);
    }

    public void RemoveArg(BasicBlock block, bool removeTrivialPhi) 
        => RemoveArg(FindArgIndex(block), removeTrivialPhi);

    private int FindArgIndex(Value operand, bool isValue = false)
    {
        for (int i = isValue ? 1 : 0; i < _operands.Length - 1; i += 2) {
            if (_operands[i] == operand) {
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
            string opening = (i == 0) ? " [" : ", [";
            ctx.Print($"{opening}{block}: {value}]");
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
        Ensure.That(args.Length > 0, "Phi argument array cannot be empty");

        var opers = new Value[args.Length * 2];

        for (int i = 0; i < args.Length; i++) {
            var (block, value) = args[i];
            opers[i * 2 + 0] = block;
            opers[i * 2 + 1] = value;
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