namespace DistIL.IR.DSL.Matchers;

public class BinaryMatcher : IInstructionMatcher
{
    public bool Match(Instruction instruction, ValueMatchInterpolator outputs, InstructionPattern pattern)
    {
        if (pattern.Arguments.Count == 2 && instruction is BinaryInst bin) {
            var operation = Enum.Parse<BinaryOp>(pattern.Operation, true);

            if (operation != bin.Op) {
                return false;
            }



            outputs.SetValue(0, bin.Left);
            outputs.SetValue(1, bin.Right);

            return true;
        }

        return false;
    }
}