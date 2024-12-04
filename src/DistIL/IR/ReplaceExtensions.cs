namespace DistIL.IR;

using DSL;
using DSL.PatternArguments;

using Utils.Parser;

public static class ReplaceExtensions
{
    public static void Replace(this Instruction instruction, ReadOnlySpan<char> replacementPattern)
    {
        var parts = new Range[2];
        replacementPattern.Split(parts, "->", StringSplitOptions.TrimEntries);

        var outputs = new OutputPattern(replacementPattern[parts[0]]);
        var matched = instruction.MatchInstruction(outputs.Pattern!, outputs);

        if (matched) {
            var pattern = InstructionPattern.Parse(replacementPattern[parts[1]]);
            var newInstr = Evaluate(pattern, outputs);
            instruction.ReplaceUses(newInstr);

            if (instruction.NumUses == 0) {
                instruction.Remove();
            }
        }
    }

    private static Value Evaluate(IInstructionPatternArgument replacementPattern, OutputPattern outputs)
    {
        return replacementPattern switch {
            BufferArgument b => outputs.Get(b.Name)!,
            OutputArgument o => outputs.Get(o.Name)!,
            InstructionPattern instr => CreateInstruction(instr, outputs),
            ConstantArgument constant => CreateConstant(constant),
            _ => throw new ArgumentException($"Invalid replacement pattern type: {replacementPattern.GetType()}")
        };
    }

    private static Value CreateConstant(ConstantArgument constant)
    {
        if (constant.Type == PrimType.Single) {
            return  ConstFloat.CreateS((float)constant.Value);
        }
        if (constant.Type == PrimType.Double) {
            return  ConstFloat.CreateD((double)constant.Value);
        }
        if (constant.Type == PrimType.Int32) {
            return ConstInt.CreateI((int)constant.Value);
        }
        if (constant.Type == PrimType.Int64) {
            return ConstInt.CreateL((long)constant.Value);
        }

        throw new ArgumentOutOfRangeException(nameof(constant.Type));
    }

    private static Value CreateInstruction(InstructionPattern instr, OutputPattern outputs)
    {
        var args = instr.Arguments.Select(a => Evaluate(a, outputs)).ToArray();

        if (instr.OpCode is > Opcode._Bin_First and < Opcode._Bin_Last) {
            var binOp = (BinaryOp)(instr.OpCode - (Opcode._Bin_First + 1));
            return new BinaryInst(binOp, args[0], args[1]);
        }
        else if (instr.OpCode is > Opcode._Cmp_First and < Opcode._Cmp_Last) {
            var cmpOp = (BinaryOp)(instr.OpCode - (Opcode._Cmp_First + 1));
            return new BinaryInst(cmpOp, args[0], args[1]);
        }

        throw new ArgumentException("Invalid instruction opcode");
    }
}