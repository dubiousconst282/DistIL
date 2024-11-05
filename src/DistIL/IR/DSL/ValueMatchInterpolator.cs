namespace DistIL.IR.DSL;

using System.Runtime.CompilerServices;

[InterpolatedStringHandler]
public unsafe struct ValueMatchInterpolator(int literalLength, int formattedCount)
{
    public readonly Dictionary<string, IntPtr> Outputs = [];
    private readonly StringBuilder _builder = new StringBuilder();

    public void AppendLiteral(string value)
    {
        _builder.Append(value);
    }

    public void AppendFormatted<T>(in T value, [CallerArgumentExpression("value")] string? name = null)
        where T : Value
    {
        if (name != null)
        {
            Outputs[name] = (IntPtr)Unsafe.AsPointer(ref Unsafe.AsRef(in value));
            _builder.Append("{" + name + "}");
        }
    }

    public string GetPattern() {
        return _builder.ToString();
    }

    public void SetValue(int index, Value value)
    {
        if (index > Outputs.Count) {
            return;
        }

        var key = Outputs.Keys.ElementAt(index);
        var ptr = Outputs[key];

        *((Value*)ptr) = value;
    }
}