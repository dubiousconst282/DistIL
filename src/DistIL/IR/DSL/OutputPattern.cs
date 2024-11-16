namespace DistIL.IR.DSL;

public readonly struct OutputPattern
{
    private readonly Dictionary<string, Value> _outputs = [];
    private readonly Dictionary<string, Value> _buffer = [];
    internal readonly InstructionPattern? Pattern = null;

    public OutputPattern(string input)
    {
        Pattern = InstructionPattern.Parse(input);
    }

    internal void Add(string key, Value value)
    {
        _outputs[key] = value;
    }

    internal void AddToBuffer(string key, Value value)
    {
        _buffer[key] = value;
    }

    public Value this[string name] => _outputs[name];

    public Value this[int position] {
        get {
            string name = _outputs.Keys.ElementAt(position);

            return _outputs[name];
        }
    }

    internal Value GetFromBuffer(string name)
    {
        return _buffer[name];
    }

    internal bool IsValueInBuffer(string name)
    {
        return _buffer.ContainsKey(name);
    }
}