namespace DistIL.IR.DSL;

[InterpolatedStringHandler]
internal ref struct ValueMatchInterpolator(int literalLength, int formattedCount)
{
    public readonly Dictionary<string, Value> Outputs = [];
    private StringBuilder _builder = new StringBuilder();

    public void AppendLiteral(string value)
    {
        _builder.Append(value);
    }

    public void AppendFormatted<T>(in T value, [CallerArgumentExpression("value")] string? name = null)
        where T : Value
    {
        if (name != null)
        {
            Outputs[name] = new ConstInt(Outputs.Count);
            Unsafe.AsRef(value) = (T)Outputs[name];
            _builder.Append("{" + name + "}");
        }
    }

    public Value? GetOutput(string name)
    {
        return Outputs.TryGetValue(name, out var value) ? value : null;
    }
        
    public string GetPattern() => _builder.ToString();
}