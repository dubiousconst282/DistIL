namespace DistIL.IR;

using DSL;
using DSL.PatternArguments;

using Utils.Parser;

public static class MatchExtensions
{
    public static bool Match(this Instruction instruction, ValueMatchInterpolator outputs)
    {
        var instrPattern = InstructionPattern.Parse(outputs.GetPattern());

        if (MatchInstruction(instruction, instrPattern, outputs)) {
            outputs.ApplyOutputs();
            return true;
        }

        return false;
    }

    private static bool MatchInstruction(Instruction instruction, InstructionPattern instrPattern, ValueMatchInterpolator outputs)
    {
        if (instrPattern.Arguments.Count == 2 && instruction is BinaryInst bin) {
            return MatchBinary(bin, instrPattern, outputs);
        }

        return false;
    }

    private static bool MatchArgument(Value value, IInstructionPatternArgument argument, ValueMatchInterpolator outputs)
    {
        switch (argument)
        {
            case IgnoreArgument:
                return true;
            case OutputArgument output:
                outputs.AddToOutputBuffer(output.Name, value);
                return true;
            case ConstantArgument number when value is Const constant:
                return MatchNumberArgument(number, constant);
            case InstructionPattern pattern:
                return MatchValue(value, pattern, outputs);
            default:
                return false;
        }
    }

    private static bool MatchValue(Value value, IInstructionPatternArgument pattern, ValueMatchInterpolator outputs)
    {
        return pattern switch {
            InstructionPattern p when value is Instruction instruction  => MatchInstruction(instruction, p, outputs),
            _ => MatchArgument(value, pattern, outputs)
        };
    }

    private static bool MatchNumberArgument(ConstantArgument constantArg, Const constant)
    {
        if (constantArg.Type == constant.ResultType) {
            object? value = constant switch {
                ConstInt constInt => constInt.Value,
                ConstFloat constFloat => constFloat.Value,
                ConstNull => null,
                _ => null
            };

            return value.Equals(constantArg.Value);
        }

        return false;
    }

    private static bool MatchBinary(BinaryInst bin, InstructionPattern pattern, ValueMatchInterpolator outputs)
    {
        var operation = pattern.Operation;
        var op = (BinaryOp)(operation - (Opcode._Bin_First + 1));

        if (bin.Op != op) {
            return false;
        }

        bool left = MatchValue(bin.Left, pattern.Arguments[0], outputs);
        bool right = MatchValue(bin.Right, pattern.Arguments[1], outputs);

        return left && right;
    }
}