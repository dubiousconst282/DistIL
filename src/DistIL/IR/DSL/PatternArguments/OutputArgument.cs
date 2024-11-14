namespace DistIL.IR.DSL.PatternArguments;

internal record OutputArgument(string Name, IInstructionPatternArgument? SubPattern = null) : IInstructionPatternArgument
{

}