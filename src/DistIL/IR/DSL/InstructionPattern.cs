namespace DistIL.IR.DSL;

using System;
using System.Collections.Generic;

using PatternArguments;
using Utils.Parser;

internal record InstructionPattern(
    Opcode OpCode,
    string Operation,
    List<IInstructionPatternArgument> Arguments)
    : IInstructionPatternArgument
{
    public static IInstructionPatternArgument? Parse(ReadOnlySpan<char> pattern)
    {
        // Remove whitespace and validate parentheses balance
        pattern = pattern.Trim();
        if (pattern.Length == 0) {
            return null;
        }

        if (pattern[0] != '(' || pattern[^1] != ')')
            return ParseArgument(pattern);

        // Remove the outer parentheses
        pattern = pattern[1..^1].Trim();

        // Split the operation from its arguments
        int spaceIndex = pattern.IndexOf(' ');
        if (spaceIndex == -1) {
            spaceIndex = pattern.Length;
        }

        var op = pattern[..spaceIndex].ToString();
        var operation = Opcodes.TryParse(op); // TryParse does not support span yet
        var argsString = pattern[spaceIndex..].Trim();

        List<IInstructionPatternArgument> arguments = new List<IInstructionPatternArgument>();

        if (operation.Op is Opcode.Call or Opcode.CallVirt) {
            var selector = argsString[..argsString.IndexOf(' ')].ToString();
            arguments.Add(new MethodRefArgument(selector));

            argsString = argsString[argsString.IndexOf(' ')..];
        }

        ParseArguments(argsString, arguments);

        return new InstructionPattern(operation.Op, op, arguments);
    }

    private static IInstructionPatternArgument? ParseEval(ReadOnlySpan<char> pattern)
    {
        var op = pattern[1..].Trim();

        return new EvalArgument(ParseArgument(op));
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

    private static IInstructionPatternArgument ParseArgument(ReadOnlySpan<char> arg)
    {
        if (arg[0] == '(' && arg[^1] == ')') {
            return Parse(arg)!;
        }

        if (arg.Contains('#'))
        {
            var left = arg[..arg.IndexOf('#')];
            var typeSpecifier = arg[arg.IndexOf('#')..].TrimStart('#');

            var argument = left is not "" ? ParseArgument(left) : null;
            return new TypedArgument(argument, typeSpecifier.ToString());
        }

        if (arg[0] == '!') {
            return ParseNot(arg);
        }

        if (arg[0] == '$') {
            return ParseBuffer(arg);
        }

        if (arg[0] == '<' || arg[0] == '>') {
            return ParseNumOperator(arg);
        }

        if (arg[0] == '#') {
            return new TypedArgument(default, arg[1..].ToString());
        }

        if (arg[0] == '*' || arg[0] == '\'')
        {
            return ParseStringArgument(arg);
        }

        if (arg[0] == '{' && arg[^1] == '}') {
            return ParseOutputArgument(arg);
        }

        if (arg[0] == '_')
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

    private static IInstructionPatternArgument ParseOutputArgument(ReadOnlySpan<char> arg)
    {
        arg = arg[1..^1];

        if (arg.Contains(':')) {
            var name = arg[..arg.IndexOf(':')];
            var subPattern = ParseArgument(arg[(arg.IndexOf(':') + 1)..]);

            return new OutputArgument(name.ToString(), subPattern);
        }

        return new OutputArgument(arg.ToString());
    }

    private static IInstructionPatternArgument ParseBuffer(ReadOnlySpan<char> arg)
    {
        return new BufferArgument(arg[1..].ToString());
    }

    private static IInstructionPatternArgument ParseNumOperator(ReadOnlySpan<char> arg)
    {
        var op = arg[0];

        return new NumberOperatorArgument(op, ParseArgument(arg[1..]));
    }


    private static IInstructionPatternArgument ParseNot(ReadOnlySpan<char> arg)
    {
        var trimmed = arg.TrimStart('!');

        return new NotArgument(ParseArgument(trimmed));
    }

    private static IInstructionPatternArgument ParseStringArgument(ReadOnlySpan<char> arg)
    {
        StringOperation operation = StringOperation.None;

        if (arg[0] == '*' && arg[^1] == '*') {
            operation = StringOperation.Contains;
        }
        else if(arg[0] == '*') {
            operation = StringOperation.EndsWith;
        }
        else if(arg[^1] == '*') {
            operation = StringOperation.StartsWith;
        }
        
        arg = arg.TrimStart('*').TrimEnd('*');

        if (arg[0] == '\'' && arg[^1] == '\'') {
            return new StringArgument(arg[1..^1].ToString(), operation);
        }

        throw new ArgumentException("Invalid string");
    }

}