using System.IO;
using DistIL.AsmIO;
using DistIL.Frontend;
using DistIL.IR.Utils;
using DistIL.CodeGen.Cil;

var resolver = new ModuleResolver();
var mod = resolver.Load("../TestSamples/CsSamples/bin/Debug/IRTests.dll");

var methods = mod.TypeDefs.SelectMany(t => t.Methods);

foreach (var method in methods) {
    try {
        //if (method.Body!.ExceptionRegions.Count > 0) continue;
        var imp = new ILImporter(method);
        method.Body = imp.ImportCode();
    } catch (Exception ex) {
        Console.WriteLine($"FailImp: {method} {ex.Message}");
    }
}

foreach (var method in methods) {
    var body = method.Body;
    if (body == null) continue;
    new DistIL.Passes.MergeBlocks().Transform(body);
    new DistIL.Passes.InlineMethods().Transform(body);
}

foreach (var method in methods) {
    var body = method.Body;
    if (body == null) continue;
    new DistIL.Passes.SsaTransform2().Transform(body);
    new DistIL.Passes.SimplifyConds().Transform(body);
    new DistIL.Passes.ConstFold().Transform(body);
    new DistIL.Passes.DeadCodeElim().Transform(body);
    new DistIL.Passes.MergeBlocks().Transform(body);

    new DistIL.Passes.ValueNumbering().Transform(body);

    if (method.Name == "UpdateBodies_A") {
        File.WriteAllLines("../../logs/code_il.txt", method.ILBody!.Instructions.Select(s => s.ToString()));
        IRPrinter.ExportPlain(body, "../../logs/code.txt");
        IRPrinter.ExportDot(body, "../../logs/cfg.dot");
    }

    //IRPrinter.ExportPlain(method, "../../logs/code_noPhis.txt");

    try {
        new DistIL.Passes.RemovePhis().Transform(body);
        new ILGenerator().EmitMethod(method);
    } catch (Exception ex) {
        Console.WriteLine($"FailEmit: {method} {ex.Message}");
    }
}

using var outStream = File.Create("../../logs/WriterOut.dll");
mod.Save(outStream);