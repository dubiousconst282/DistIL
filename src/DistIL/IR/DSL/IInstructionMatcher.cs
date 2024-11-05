namespace DistIL.IR;

using DSL;

public interface IInstructionMatcher
{
    bool Match(Instruction instruction, ValueMatchInterpolator outputs, InstructionPattern pattern);
}