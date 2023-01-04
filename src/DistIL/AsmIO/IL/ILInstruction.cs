namespace DistIL.AsmIO;

public struct ILInstruction
{
    public ILCode OpCode { get; set; }
    public int Offset { get; set; }
    /// <summary> Operand value. One of: <c>null, int, long, float, double, EntityDesc, or int[]</c>. </summary>
    public object? Operand { get; set; }

    public ILOperandType OperandType => OpCode.GetOperandType();
    public ILFlowControl FlowControl => OpCode.GetFlowControl();

    public ILInstruction(ILCode op, object? operand = null)
    {
        OpCode = op;
        Offset = 0;
        Operand = operand;
    }

    public int GetSize()
    {
        int operSize = OpCode switch {
            ILCode.Switch => 4 + ((Array)Operand!).Length * 4,
            _ => OpCode.GetOperandType().GetSize()
        };
        return OpCode.GetSize() + operSize;
    }
    public int GetEndOffset()
    {
        return Offset + GetSize();
    }

    public override string ToString()
    {
        string? operandStr = OpCode.GetOperandType() switch {
            ILOperandType.None => null,
            ILOperandType.BrTarget or
            ILOperandType.ShortBrTarget when Operand is int targetOffset
                => $"IL_{targetOffset:X4}",
            _ => Operand?.ToString()
        };
        return $"IL_{Offset:X4}: {OpCode.GetName()}{(operandStr == null ? "" : " ")}{operandStr}";
    }
}
