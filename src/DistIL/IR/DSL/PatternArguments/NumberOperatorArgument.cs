namespace DistIL.IR.DSL.PatternArguments;

internal record NumberOperatorArgument(char Operator, IInstructionPatternArgument Argument) : IInstructionPatternArgument
{

}