using System.IO;
using DistIL.AsmIO;
using DistIL.Frontend;
using DistIL.IR.Utils;
using DistIL.CodeGen.Cil;
using DistIL.Passes;

var resolver = new ModuleResolver();
var module = resolver.Load("../TestSamples/CsSamples/bin/Debug/IRTests.dll");

var mp = new MethodPassManager();
mp.Add(new SsaTransform2());
mp.Add(new SimplifyConds());
mp.Add(new ConstFold());
mp.Add(new DeadCodeElim());
mp.Add(new MergeBlocks());
mp.Add(new ValueNumbering());
mp.Add(new PrintPass());
//mp.Add(new RemovePhis());

var modPm = new ModulePassManager();
modPm.Add(new ImportPass());
modPm.Add(mp);
modPm.Add(new ExportPass());

modPm.Run(module);

using var outStream = File.Create("../../logs/WriterOut.dll");
module.Save(outStream);


class PrintPass : MethodPass
{
    public override void Run(MethodTransformContext ctx)
    {
        if (ctx.Method.Definition.Name == "ObjAccess") {
            IRPrinter.ExportPlain(ctx.Method, "../../logs/code.txt");
            IRPrinter.ExportDot(ctx.Method, "../../logs/cfg.dot");
        }
    }
}

class ImportPass : ModulePass
{
    public override void Run(ModuleTransformContext ctx)
    {
        foreach (var method in ctx.Module.AllMethods()) {
            try {
                //if (method.Body!.ExceptionRegions.Count > 0) continue;
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
            try {
                new ILGenerator().EmitMethod(method.Definition);
            } catch (Exception ex) {
                Console.WriteLine($"FailEmit: {method} {ex.Message}");
            }
        }
    }
}