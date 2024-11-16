namespace DistIL.IR;

using DSL.PatternArguments;
using DSL;
using Utils.Parser;

public static class MatchExtensions
{
    public static bool Match(this Instruction instruction, string pattern, out OutputPattern outputs)
    {
        outputs = new OutputPattern(pattern);

        return MatchInstruction(instruction, outputs.Pattern!, outputs);
    }

    public static bool Match(this Instruction instruction, string pattern)
    {
        var outputs = new OutputPattern(pattern);

        return MatchInstruction(instruction, outputs.Pattern!, outputs);
    }

    private static bool MatchInstruction(Instruction instruction, InstructionPattern instrPattern, OutputPattern outputs)
    {
        if (instruction is BinaryInst bin) {
            return MatchBinary(bin, instrPattern, outputs);
        }
        if (instruction is CompareInst comp) {
            return MatchCompare(comp, instrPattern, outputs);
        }
        else if (instruction is UnaryInst un) {
            return MatchUnary(un, instrPattern, outputs);
        }

        return MatchOtherInstruction(instruction, instrPattern, outputs);
    }

    private static bool MatchOtherInstruction(Instruction instruction, InstructionPattern pattern, OutputPattern outputs)
    {
        var op = pattern.OpCode;
        var ops = MatchOpCode((op, instruction));

        return MatchOperands(instruction, pattern, outputs);
    }

    private static bool MatchOpCode((Opcode op, Instruction instruction) opInstrTuple)
    {
        return opInstrTuple switch {
            (Opcode.Unknown, _) => false,
            (Opcode.Goto, BranchInst) => true,
            (Opcode.Switch, SwitchInst) => true,
            (Opcode.Ret, ReturnInst) => true,
            (Opcode.Phi, PhiInst) => true,
            (Opcode.Select, SelectInst) => true,
            (Opcode.Lea, PtrOffsetInst) => true,
            (Opcode.Getfld,FieldExtractInst) => true,
            (Opcode.Setfld, FieldInsertInst) => true,
            (Opcode.ArrAddr, ArrayAddrInst) => true,
            (Opcode.FldAddr, FieldAddrInst) => true,
            (Opcode.Load, LoadInst) => true,
            (Opcode.Store, StoreInst) => true,
            (Opcode.Conv, ConvertInst) => true,
            _ => false
        };
    }

    private static bool MatchOperands(Instruction instruction, InstructionPattern pattern, OutputPattern outputs)
    {
        bool matched = true;

        if (pattern.Arguments.Count > instruction.Operands.Length) {
            return false;
        }

        for (int index = 0; index < pattern.Arguments.Count; index++) {
            Value? operand = instruction.Operands[index];
            matched &= MatchValue(operand, pattern.Arguments[index], outputs);
        }

        return matched;
    }

    private static bool MatchArgument(Value value, IInstructionPatternArgument argument, OutputPattern outputs)
    {
        switch (argument) {
            case NotArgument not:
                return !MatchArgument(value, not.Inner, outputs);
            case IgnoreArgument:
                return true;
            case BufferArgument buffer:
                return MatchBuffer(value, buffer, outputs);
            case OutputArgument output:
                return MatchOutput(value, outputs, output);
            case ConstantArgument constArg when value is Const constant:
                return MatchConstArgument(constArg, constant);
            case InstructionPattern pattern:
                return MatchValue(value, pattern, outputs);
            case TypedArgument typed:
                return MatchTypeSpecifier(value, typed, outputs);
            case NumberOperatorArgument numOp:
                return MatchNumOperator(value, numOp, outputs);
            default:
                return false;
        }
    }

    private static bool MatchOutput(Value value, OutputPattern outputs, OutputArgument output)
    {
        if (output.SubPattern is null) {
            outputs.Add(output.Name, value);
            return true;
        }

        if (MatchValue(value, output.SubPattern, outputs)) {
            outputs.Add(output.Name, value);
            return true;
        }

        return false;
    }

    private static bool MatchBuffer(Value value, BufferArgument buffer, OutputPattern outputs)
    {
        if (outputs.IsValueInBuffer(buffer.Name)) {
            var bufferedValue = outputs.GetFromBuffer(buffer.Name);

            return bufferedValue == value;
        }

        outputs.AddToBuffer(buffer.Name, value);
        return true;
    }

    private static bool MatchNumOperator(Value value, NumberOperatorArgument numOp, OutputPattern outputs)
    {
        if (numOp.Argument is not ConstantArgument constantArg) {
            return false;
        }

        if (constantArg.Type != PrimType.Int32 && constantArg.Type != PrimType.Double) {
            return false;
        }

        dynamic constant = constantArg;
        dynamic val = value;

        if (numOp.Operator == '<') {
            return val.Value < constant.Value;
        } else if (numOp.Operator == '>') {
            return val.Value > constant.Value;
        }

        return false;
    }


    private static bool MatchTypeSpecifier(Value value, TypedArgument typed, OutputPattern outputs)
    {
        bool result = true;
        if (typed.Argument is not null) {
            result = MatchArgument(value, typed.Argument, outputs);
        }

        if (typed.Type is "const") {
            result &= value is Const;
        } else if (typed.Type is "instr") {
            result &= value is Instruction;
        } else {
            result &= PrimType.GetFromAlias(typed.Type) == value.ResultType;
        }

        return result;
    }

    private static bool MatchValue(Value value, IInstructionPatternArgument pattern, OutputPattern outputs)
    {
        return pattern switch {
            InstructionPattern p when value is Instruction instruction => MatchInstruction(instruction, p, outputs),
            _ => MatchArgument(value, pattern, outputs)
        };
    }

    private static bool MatchConstArgument(ConstantArgument constantArg, Const constant)
    {
        if (constantArg.Type == constant.ResultType) {
            if (constantArg is StringArgument strArg) {
                return MatchStringArg(strArg, constant as ConstString);
            }

            object? value = constant switch {
                ConstInt constInt => constInt.Value,
                ConstFloat constFloat => constFloat.Value,
                ConstNull => null,
                _ => null
            };

            return value.Equals(constantArg.Value);
        }

        return false;
    }

    private static bool MatchStringArg(StringArgument strArg, ConstString constant)
    {
        if (strArg.Operation == StringOperation.StartsWith) {
            return constant.Value.StartsWith(strArg.Value.ToString()!);
        }
        if (strArg.Operation == StringOperation.EndsWith) {
            return constant.Value.EndsWith(strArg.Value.ToString()!);
        }
        if (strArg.Operation == StringOperation.Contains) {
            return constant.Value.Contains(strArg.Value.ToString()!);
        }

        return strArg.Value.Equals(constant.Value);
    }


    private static bool MatchBinary(BinaryInst bin, InstructionPattern pattern, OutputPattern outputs)
    {
        var operation = pattern.OpCode;
        var op = (BinaryOp)(operation - (Opcode._Bin_First + 1));

        if (bin.Op != op) {
            return false;
        }

        return MatchOperands(bin, pattern, outputs);
    }

    private static bool MatchCompare(CompareInst comp, InstructionPattern pattern, OutputPattern outputs)
    {
        var operation = pattern.OpCode;
        var op = (CompareOp)(operation - (Opcode._Cmp_First + 1));

        if (comp.Op != op) {
            return false;
        }

        return MatchOperands(comp, pattern, outputs);
    }

    private static bool MatchUnary(UnaryInst un, InstructionPattern pattern, OutputPattern outputs)
    {
        var operation = pattern.OpCode;
        var op = (UnaryOp)(operation - (Opcode._Bin_First + 1));

        if (un.Op != op) {
            return false;
        }

        return MatchOperands(un, pattern, outputs);;
    }
}