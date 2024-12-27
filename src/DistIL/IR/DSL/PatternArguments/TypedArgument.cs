namespace DistIL.IR.DSL.PatternArguments;

internal record TypedArgument(IInstructionPatternArgument? Argument, string Type) : IInstructionPatternArgument
{

}
