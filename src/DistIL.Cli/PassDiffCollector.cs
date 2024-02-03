using System.Text.Json;
using System.Text.Json.Serialization;

using DistIL;
using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;
using DistIL.Passes;

class PassDiffCollector(string outputPath, Predicate<MethodDef>? filter) : IPassInspector
{
    Dictionary<MethodDef, MethodTimeline> _entries = new();

    void IPassInspector.OnBeforePass(IMethodPass pass, MethodTransformContext ctx)
    {
        var method = ctx.Method.Definition;

        if (filter != null && !filter.Invoke(method)) return;

        if (!_entries.ContainsKey(method)) {
            var entry = new MethodTimeline() { Name = method.DeclaringType.Name + "::" + method.Name };
            entry.TakeSnapshot(method, "(initial)", captureCode: true);

            _entries.Add(method, entry);
        }
    }
    void IPassInspector.OnAfterPass(IMethodPass pass, MethodTransformContext ctx, MethodPassResult result)
    {
        var method = ctx.Method.Definition;

        if (_entries.TryGetValue(method, out var entry)) {
            bool changed = result.Changes != MethodInvalidations.None;

            // sanity hash check
            if (!changed) {
                int newHash = ComputeHash(method.Body!);
                changed = newHash != entry.LastHash;
                entry.LastHash = newHash;
            }
            entry.TakeSnapshot(method, pass.GetType().Name, captureCode: changed);
        }
    }

    void IPassInspector.OnFinish(Compilation comp)
    {
        using var fs = File.Create(outputPath);

        var opts = new JsonSerializerOptions() {
            IncludeFields = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };
        JsonSerializer.Serialize(fs, _entries.Values, opts);
        
        comp.Logger.Info($"Collected pass dumps for {_entries.Count} methods.");
    }

    private static int ComputeHash(MethodBody body)
    {
        var hc = new HashCode();
        hc.Add(body.NumBlocks);

        foreach (var inst in body.Instructions()) {
            hc.Add(inst);

            foreach (var oper in inst.Operands) {
                hc.Add(oper);
            }
        }
        return hc.ToHashCode();
    }

    class MethodTimeline
    {
        public string Name = "";
        public List<MethodSnapshot> Passes = new();

        [JsonIgnore] public int LastHash;

        public void TakeSnapshot(MethodDef method, string passName, bool captureCode)
        {
            var sn = new MethodSnapshot() { PassName = passName };

            if (captureCode) {
                var sw = new StringWriter();
                IRPrinter.ExportPlain(method.Body!, sw);
                sn.PlainCode = sw.ToString();

                sw.GetStringBuilder().Clear();

                IRPrinter.ExportDot(method.Body!, sw);
                sn.GraphvizCode = sw.ToString();
            }
            Passes.Add(sn);
        }
    }
    struct MethodSnapshot
    {
        public string PassName;
        public string? PlainCode;
        public string? GraphvizCode;
    }
}