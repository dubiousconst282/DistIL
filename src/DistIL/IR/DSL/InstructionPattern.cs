﻿namespace DistIL.IR.DSL;

using System;
using System.Collections.Generic;

using DistIL.IR.DSL.PatternArguments;
using DistIL.IR.Utils.Parser;

internal record InstructionPattern(Opcode Operation, List<IInstructionPatternArgument> Arguments)
    : IInstructionPatternArgument
{
    public static InstructionPattern? Parse(ReadOnlySpan<char> pattern)
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

        var operation = Opcodes.TryParse(pattern[..spaceIndex].ToString()); // TryParse does not support span yet
        var argsString = pattern[spaceIndex..].Trim();

        List<IInstructionPatternArgument> arguments = new List<IInstructionPatternArgument>();
        ParseArguments(argsString, arguments);

        return new InstructionPattern(operation.Op, arguments);
    }

    private static void ParseArguments(ReadOnlySpan<char> argsString, List<IInstructionPatternArgument> arguments)
    {
        int depth = 0;
        string currentArg = "";
        Stack<char> outputStack = new();

        foreach (var c in argsString)
        {
            if (c == '{') {
                outputStack.Push(c);
            }
            else if (c == '}') {
                outputStack.Pop();
            }

            if (c == '(')
            {
                depth++;
                currentArg += c;
            }
            else if (c == ')')
            {
                depth--;
                currentArg += c;
                if (depth == 0 && outputStack.Count == 0)
                {
                    // Completed a nested argument
                    arguments.Add(Parse(currentArg.AsSpan())!);
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
        if (arg.StartsWith('(') && arg.EndsWith(')')) {
            return Parse(arg.AsSpan())!;
        }

        if (arg.Contains('#'))
        {
            var left = arg[..arg.IndexOf('#')];
            var typeSpecifier = arg[arg.IndexOf('#')..].TrimStart('#');

            var argument = left != "" ? ParseArgument(left) : null;
            return new TypedArgument(argument, typeSpecifier);
        }

        if (arg.StartsWith('!')) {
            return ParseNot(arg);
        }

        if (arg.StartsWith('$')) {
            return ParseBuffer(arg);
        }

        if (arg.StartsWith('<') || arg.StartsWith('>')) {
            return ParseNumOperator(arg);
        }

        if (arg.StartsWith('#')) {
            return new TypedArgument(default, arg[1..]);
        }

        if (arg.StartsWith('*') || arg.StartsWith('\''))
        {
            return ParseStringArgument(arg);
        }

        if (arg.StartsWith('{') && arg.EndsWith('}')) {
            return ParseOutputArgument(arg);
        }

        if (arg == "_")
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

        throw new ArgumentException("Invalid Argument");
    }

    private static IInstructionPatternArgument ParseOutputArgument(string arg)
    {
        arg = arg[1..^1];

        if (arg.Contains(':')) {
            var name = arg[..arg.IndexOf(':')];
            var subPattern = ParseArgument(arg[(arg.IndexOf(':') + 1)..]);

            return new OutputArgument(name, subPattern);
        }

        return new OutputArgument(arg);
    }

    private static IInstructionPatternArgument ParseBuffer(string arg)
    {
        return new BufferArgument(arg[1..]);
    }

    private static IInstructionPatternArgument ParseNumOperator(string arg)
    {
        var op = arg[0];

        return new NumberOperatorArgument(op, ParseArgument(arg[1..]));
    }


    private static IInstructionPatternArgument ParseNot(string arg)
    {
        var trimmed = arg.TrimStart('!');

        return new NotArgument(ParseArgument(trimmed));
    }

    private static IInstructionPatternArgument ParseStringArgument(string arg)
    {
        StringOperation operation = StringOperation.None;

        if (arg.StartsWith('*') && arg.EndsWith('*')) {
            operation = StringOperation.Contains;
        }
        else if(arg.StartsWith('*')) {
            operation = StringOperation.EndsWith;
        }
        else if(arg.EndsWith('*')) {
            operation = StringOperation.StartsWith;
        }
        
        arg = arg.TrimStart('*').TrimEnd('*');

        if (arg.StartsWith('\'') && arg.EndsWith('\'')) {
            return new StringArgument(arg[1..^1], operation);
        }

        throw new ArgumentException("Invalid string");
    }

}