using System.Text.RegularExpressions;

using DistIL.AsmIO;
using DistIL.CodeGen.Cil;
using DistIL.Frontend;
using DistIL.IR.Utils;
using DistIL.Passes;

if (args.Length == 0) {
    //Set launch args on VS: Proj Settings > Debug > Cmd Args | VSCode: launch.json
    //(you can also hardcode them here, whatever.)
    Console.WriteLine($"Arguments: <input module path> [output module path] [ir dump dir] [dump filter regex]");
    return;
}

var resolver = new ModuleResolver();
var module = resolver.Load(args[0]);

var mp1 = new MethodPassManager();
mp1.Add(new SimplifyCFG());
mp1.Add(new SsaTransform());

var mp2 = new MethodPassManager();
mp2.Add(new ExpandLinq());
mp2.Add(new SimplifyInsts());
mp2.Add(new InlineMethods());
mp2.Add(new ConstFold());
mp2.Add(new SimplifyCFG());
mp2.Add(new DeadCodeElim());
mp2.Add(new ValueNumbering());
mp2.Add(new SimplifyCFG());
mp2.Add(new DeadCodeElim());
if (args.Length >= 3) {
    mp2.Add(new DumpPass() {
        Directory = args[2], 
        Filter = args.Length >= 4 ? args[3] : null
    });
}
mp2.Add(new RemovePhis());

var modPm = new ModulePassManager();
modPm.Add(new ImportPass());
modPm.Add(mp1);
modPm.Add(mp2);
modPm.Add(new ExportPass());

modPm.Run(module);

if (args.Length >= 2) {
    using var outStream = File.Create(args[1]);
    module.Save(outStream);
}

class DumpPass : MethodPass
{
    public string? Directory { get; init; }
    public string? Filter { get; init; }

    public override void Run(MethodTransformContext ctx)
    {
        var diags = Verifier.Diagnose(ctx.Method);
        if (diags.Count > 0) {
            Console.WriteLine($"BadIR in {ctx.Method}: {string.Join(" | ", diags)}");
        }
        var def = ctx.Method.Definition;
        string name = $"{def.DeclaringType.Name}::{def.Name}";
        if (Filter == null || Regex.IsMatch(name, Filter)) {
            var invalidNameChars = Path.GetInvalidFileNameChars();
            name = new string(name.Select(c => Array.IndexOf(invalidNameChars, c) < 0 ? c : '_').ToArray());

            IRPrinter.ExportPlain(ctx.Method, $"{Directory}/{name}.txt");
            IRPrinter.ExportDot(ctx.Method, $"{Directory}/{name}.dot");
        }
    }
}

class ImportPass : ModulePass
{
    public override void Run(ModuleTransformContext ctx)
    {
        foreach (var method in ctx.Module.AllMethods()) {
            if (method.ILBody == null) continue;
            try {
                var imp = new ILImporter(method);
                method.Body = imp.ImportCode();
            } catch (Exception ex) {
                Console.WriteLine($"FailImp: {method} {ex.Message}");
            }
        }
    }
}
class ExportPass : ModulePass
{
    public override void Run(ModuleTransformContext ctx)
    {
        foreach (var method in ctx.Module.AllMethods()) {
            if (method.Body == null) continue;

            try {
                method.ILBody = new ILGenerator(method.Body).Process();
            } catch (Exception ex) {
                Console.WriteLine($"FailEmit: {method} {ex.Message}");
            }
        }
    }
}