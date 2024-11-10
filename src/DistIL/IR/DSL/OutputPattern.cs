namespace DistIL.IR.DSL;

using System.Runtime.CompilerServices;

[InterpolatedStringHandler]
public unsafe struct OutputPattern(int literalLength, int formattedCount)
{
    private readonly Dictionary<string, IntPtr> _outputs = [];
    private readonly Dictionary<string, Value> _outputBuffer = [];
    private readonly StringBuilder _builder = new StringBuilder();
    private InstructionPattern? _pattern = null;

    public void AppendLiteral(string value)
    {
        _builder.Append(value);
    }

    public void AppendFormatted<T>(in T value, [CallerArgumentExpression("value")] string? name = null)
        where T : Value
    {
        if (name == null) {
            return;
        }

        _outputs[name] = (IntPtr)Unsafe.AsPointer(ref Unsafe.AsRef(in value));
        _builder.Append("{" + name + "}");
    }

    internal InstructionPattern? GetPattern()
    {
        return _pattern ??= InstructionPattern.Parse(_builder.ToString());
    }

    private void SetValue(int index, Value value)
    {
        if (index > _outputs.Count) {
            return;
        }

        var key = _outputs.Keys.ElementAt(index);
        SetValue(key, value);
    }

    private void SetValue(string name, Value value)
    {
        var ptr = _outputs[name];

        *((Value*)ptr) = value;
    }

    internal void Add(string key, Value value)
    {
        _outputBuffer[key] = value;
    }

    internal void Apply()
    {
        foreach (var output in _outputBuffer) {
            SetValue(output.Key, output.Value);
        }
    }
}