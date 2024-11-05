namespace DistIL.IR;

record InstructionPattern(string Operation, List<IInstructionPatternArgument> Arguments) : IInstructionPatternArgument;