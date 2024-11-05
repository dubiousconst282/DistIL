namespace DistIL.IR;

using DSL;
using DSL.PatternArguments;

public static class MatchExtensions
{
    public static bool Match(this Instruction instruction, ValueMatchInterpolator pattern)
    {
        Console.WriteLine($"Pattern: {pattern.GetPattern()}");

        TypeDesc numberType = ConstInt.CreateI(0).ResultType;
        var instrPattern = new InstructionPattern("add", [new NumberArgument(42, numberType), new NumberArgument(2, numberType)]);

        if (instruction is BinaryInst b && instrPattern.Arguments.Count == 2) {

        }

        var bin = (BinaryInst)instruction;
        pattern.SetValue(0, bin.Left);
        pattern.SetValue(1, bin.Right);

        return true;
    } 
}