namespace DistIL.IR.DSL;

public unsafe struct OutputPattern
{
    private readonly Dictionary<string, Value> _outputBuffer = [];
    internal InstructionPattern? Pattern = null;

    public OutputPattern(string input)
    {
        Pattern = InstructionPattern.Parse(input);
    }

    internal void Add(string key, Value value)
    {
        _outputBuffer[key] = value;
    }

    public Value this[string name] => _outputBuffer[name];

    public Value this[int position] {
        get {
            string name = _outputBuffer.Keys.ElementAt(position);

            return _outputBuffer[name];
        }
    }

}