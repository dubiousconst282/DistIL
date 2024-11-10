namespace DistIL.IR.DSL;

using System;
using System.Collections.Generic;

using DistIL.IR.DSL.PatternArguments;
using DistIL.IR.Utils.Parser;

internal record InstructionPattern(Opcode Operation, List<IInstructionPatternArgument> Arguments)
    : IInstructionPatternArgument
{
    public static InstructionPattern? Parse(string pattern)
    {
        // Remove whitespace and validate parentheses balance
        pattern = pattern.Trim();
        if (pattern.Length == 0) {
            return null;
        }

        if (pattern[0] != '(' || pattern[^1] != ')')
            throw new ArgumentException("Pattern must start with '(' and end with ')'.");

        // Remove the outer parentheses
        pattern = pattern[1..^1].Trim();

        // Split the operation from its arguments
        int spaceIndex = pattern.IndexOf(' ');
        if (spaceIndex == -1)
            throw new ArgumentException("Invalid pattern format.");

        var operation = Opcodes.TryParse(pattern[..spaceIndex]);
        var argsString = pattern[spaceIndex..].Trim();

        List<IInstructionPatternArgument> arguments = new List<IInstructionPatternArgument>();
        ParseArguments(argsString, arguments);

        return new InstructionPattern(operation.Op, arguments);
    }

    private static void ParseArguments(string argsString, List<IInstructionPatternArgument> arguments)
    {
        int depth = 0;
        string currentArg = "";

        foreach (var c in argsString)
        {
            if (c == '(')
            {
                depth++;
                currentArg += c;
            }
            else if (c == ')')
            {
                depth--;
                currentArg += c;
                if (depth == 0)
                {
                    // Completed a nested argument
                    arguments.Add(Parse(currentArg));
                    currentArg = "";
                }
            }
            else if (char.IsWhiteSpace(c) && depth == 0)
            {
                // End of a top-level argument
                if (!string.IsNullOrWhiteSpace(currentArg))
                {
                    arguments.Add(ParseArgument(currentArg.Trim()));
                    currentArg = "";
                }
            }
            else
            {
                currentArg += c;
            }
        }

        // Add any remaining argument
        if (!string.IsNullOrWhiteSpace(currentArg))
        {
            arguments.Add(ParseArgument(currentArg.Trim()));
        }
    }

    private static IInstructionPatternArgument ParseArgument(string arg)
    {
        if (arg.StartsWith('{') && arg.EndsWith('}'))
        {
            return new OutputArgument(arg[1..^1]);
        }

        if (arg == "?")
        {
            return new IgnoreArgument();
        }

        if (long.TryParse(arg, out var number))
        {
            return new ConstantArgument(number, PrimType.Int32);
        }
        if (double.TryParse(arg, out var dnumber))
        {
            return new ConstantArgument(dnumber, PrimType.Double);
        }

        return null;
    }
}