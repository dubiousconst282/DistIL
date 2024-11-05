namespace DistIL.IR;

using DSL;
using DSL.Matchers;
using DSL.PatternArguments;

public static class MatchExtensions
{
    private static Dictionary<Type, IInstructionMatcher> Matchers = new Dictionary<Type, IInstructionMatcher>() {
        [typeof(BinaryInst)] = new BinaryMatcher()
    };
    public static bool Match(this Instruction instruction, ValueMatchInterpolator outputs)
    {
        TypeDesc numberType = ConstInt.CreateI(0).ResultType;
        var instrPattern = new InstructionPattern("add", [new NumberArgument(42, numberType), new NumberArgument(2, numberType)]);

        Matchers[instruction.GetType()].Match(instruction, outputs, instrPattern);

        return false;
    } 
}