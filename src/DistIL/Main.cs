using System.Reflection.PortableExecutable;
using System.IO;
using DistIL.AsmIO;
using DistIL.Frontend;
using DistIL.IR.Utils;
using DistIL.CodeGen.Cil;

using var stream = File.OpenRead("../TestSamples/CsSamples/bin/Debug/IRTests.dll");
using var peReader = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);

var resolver = new ModuleResolver();
var mod = new ModuleDef(peReader, resolver);
var mdReader = mod.Reader;

foreach (var method in mod.GetDefinedMethods()) {
    try {
        if (method.Body!.ExceptionRegions.Count > 0) continue;
        var imp = new ILImporter(method);
        imp.ImportCode();
    } catch (Exception ex) {
        Console.WriteLine($"FailImp: {method} {ex.Message}");
        method.ClearBlocks();
    }
}

foreach (var method in mod.GetDefinedMethods()) {
    if (!method.CodeAvailable) continue;
    new DistIL.Passes.MergeBlocks().Transform(method);
    new DistIL.Passes.InlineMethods().Transform(method);
}

foreach (var method in mod.GetDefinedMethods()) {
    if (!method.CodeAvailable) continue;

    new DistIL.Passes.SsaTransform2().Transform(method);
    new DistIL.Passes.SimplifyConds().Transform(method);
    new DistIL.Passes.SimplifyArithm().Transform(method);
    new DistIL.Passes.ConstFold().Transform(method);
    new DistIL.Passes.DeadCodeElim().Transform(method);
    new DistIL.Passes.MergeBlocks().Transform(method);

    new DistIL.Passes.ValueNumbering().Transform(method);

    if (method.Name == "ObjAccess") {
        File.WriteAllLines("../../logs/code_il.txt", method.Body!.Instructions.Select(s => s.ToString()));
        IRPrinter.ExportPlain(method, "../../logs/code.txt");
        IRPrinter.ExportDot(method, "../../logs/cfg.dot");
    }

    //IRPrinter.ExportPlain(method, "../../logs/code_noPhis.txt");

    try {
        new DistIL.Passes.RemovePhis().Transform(method);
        new ILGenerator().EmitMethod(method);
    } catch (Exception ex) {
        Console.WriteLine($"FailEmit: {method} {ex.Message}");
    }
}

using var outStream = File.Create("../../logs/WriterOut.dll");
mod.Save(outStream);