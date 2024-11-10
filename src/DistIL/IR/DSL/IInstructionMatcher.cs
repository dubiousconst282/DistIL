namespace DistIL.IR.DSL;

internal interface IInstructionMatcher
{
    bool Match(Instruction instruction, ValueMatchInterpolator outputs, InstructionPattern pattern);
}