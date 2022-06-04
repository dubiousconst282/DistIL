using DistIL.AsmIO;
using DistIL.Frontend;
using DistIL.IR.Utils;
using DistIL.CodeGen.Cil;
using DistIL.Passes;

var resolver = new ModuleResolver();
var module = resolver.Load("../TestSamples/CsSamples/bin/Debug/IRTests.dll");

var mp1 = new MethodPassManager();
mp1.Add(new SimplifyCFG());
mp1.Add(new SsaTransform2());

var mp2 = new MethodPassManager();
mp2.Add(new InlineMethods());
mp2.Add(new ConstFold());
mp2.Add(new SimplifyCFG());
mp2.Add(new DeadCodeElim());
mp2.Add(new ValueNumbering());
mp2.Add(new SimplifyCFG());
mp2.Add(new PrintPass());
mp2.Add(new RemovePhis());

var modPm = new ModulePassManager();
modPm.Add(new ImportPass());
modPm.Add(mp1);
modPm.Add(mp2);
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
            if (method.Body == null) continue;

            try {
                new ILGenerator().EmitMethod(method.Definition);
            } catch (Exception ex) {
                Console.WriteLine($"FailEmit: {method} {ex.Message}");
            }
        }
    }
}