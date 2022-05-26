namespace DistIL.AsmIO;

public struct ILInstruction
{
    public ILCode OpCode { get; set; }
    public int Offset { get; set; }
    /// <summary> Operand value, one of `null, int, long, float, double, EntityDef, or int[]`. </summary>
    public object? Operand { get; set; }
    //We could probably use an extra long field to store primitive operands and avoid allocs, 
    //but the extra complexity may not be worth it.

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
        int operandSize = OperandType switch {
            ILOperandType.None
                => 0,
            ILOperandType.ShortBrTarget or
            ILOperandType.ShortI or
            ILOperandType.ShortVar
                => 1,
            ILOperandType.Var
                => 2,
            ILOperandType.BrTarget or
            ILOperandType.Field or
            ILOperandType.Method or
            ILOperandType.Sig or
            ILOperandType.String or
            ILOperandType.Tok or
            ILOperandType.Type or
            ILOperandType.I or
            ILOperandType.ShortR
                => 4,
            ILOperandType.I8 or
            ILOperandType.R
                => 8,
            ILOperandType.Switch
                => 4 + ((Array)Operand!).Length * 4,
            _ => throw new InvalidOperationException()
        };
        return OpCode.GetSize() + operandSize;
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
