namespace DistIL.IR;

public record InstructionPattern(string Operation, List<IInstructionPatternArgument> Arguments) : IInstructionPatternArgument;