using BenchmarkDotNet.Attributes;

public class LinqBenchs
{
    [Params(4, 128, 1024)]
    public int ElemCount {
        set {
            var rng = new Random(value - 1);

            var seq = Enumerable.Range(0, value);
            _sourceText = RandStr(value);
            _sourceItems = seq.Select(i => 
                new Item() {
                    Id = RandStr(12),
                    Timestamp = _currDate.AddDays(rng.NextDouble() * -7),
                    Weight = rng.NextSingle(),
                    Payload = new() {
                        Data = RandStr(512)
                    }
                }).ToList();

            string RandStr(int length)
            {
                var buffer = new byte[length];
                rng.NextBytes(buffer);
                return Convert.ToBase64String(buffer);
            }
        }
    }

    static readonly DateTime _currDate = new(2023, 02, 22);

    List<Item> _sourceItems = null!;
    string _sourceText = null!;

    [Benchmark]
    public List<ItemPayload> FilterObjects()
    {
        return _sourceItems
            .Where(x => x.Weight > 0.5f && x.Timestamp >= _currDate.AddDays(-3))
            .Select(x => x.Payload)
            .ToList();
    }

    [Benchmark]
    public float Aggregate()
    {
        return _sourceItems
            .Select(x => x.Weight)
            .Where(x => x > 0.0f && x < 1.0f)
            .Aggregate(0.0f, (r, x) => r + (x < 0.5f ? -x : x));
    }

    [Benchmark]
    public int CountLetters()
    {
        return _sourceText.Count(ch => ch is >= 'A' and <= 'Z');
    }

    public class Item
    {
        public string Id { get; init; } = null!;
        public DateTime Timestamp { get; init; }
        public float Weight { get; init; }
        public ItemPayload Payload { get; init; } = null!;
    }
    public class ItemPayload
    {
        public string Data { get; init; } = null!;
    }
}