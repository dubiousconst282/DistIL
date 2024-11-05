namespace DistIL.IR;

using DSL;

public static class MatchExtensions
{
    public static bool Match(this Instruction instruction, ValueMatchInterpolator pattern)
    {
        Console.WriteLine($"Pattern: {pattern.GetPattern()}");

        var bin = (BinaryInst)instruction;
        pattern.SetValue(0, bin.Left);
        pattern.SetValue(1, bin.Right);

        return true;
    } 
}