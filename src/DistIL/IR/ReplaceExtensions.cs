namespace DistIL.IR;

using DSL;
using DSL.PatternArguments;

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
            instruction.ReplaceWith(newInstr);
        }
    }

    private static Value Evaluate(IInstructionPatternArgument replacementPattern, OutputPattern outputs)
    {
        switch (replacementPattern)
        {
            case BufferArgument b:
                return outputs.Get(b.Name)!;
            case OutputArgument o:
                return outputs.Get(o.Name)!;
            case InstructionPattern instr:
                return null;
            default:
                throw new ArgumentException($"Invalid replacement pattern type: {replacementPattern.GetType()}");
        }
    }
}