namespace DistIL.IR.DSL.PatternArguments;

public record NumberArgument(object Value, TypeDesc? Type) : IInstructionPatternArgument
{

}