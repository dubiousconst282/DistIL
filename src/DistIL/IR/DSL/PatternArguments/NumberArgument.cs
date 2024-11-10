namespace DistIL.IR.DSL.PatternArguments;

internal record ConstantArgument(object Value, TypeDesc? Type) : IInstructionPatternArgument
{

}