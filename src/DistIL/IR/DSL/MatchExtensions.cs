namespace DistIL.IR;

public static class MatchExtensions
{
    public static bool Match(this Instruction instruction, ValueMatchInterpolator pattern)
    {
        Console.WriteLine($"Pattern: {pattern.GetPattern()}");
        return true;
    } 
}