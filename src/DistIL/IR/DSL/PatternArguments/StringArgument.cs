namespace DistIL.IR.DSL.PatternArguments;

internal record StringArgument(object Value, StringOperation Operation) : ConstantArgument(Value, PrimType.String)
{

}