using System.Reflection.PortableExecutable;
using System.IO;
using DistIL.AsmIO;
using DistIL.Frontend;
using DistIL.IR;
using DistIL.CodeGen.Cil;

using var stream = File.OpenRead("../TestSamples/CsSamples/bin/Debug/IRTests.dll");
using var peReader = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);

var resolver = new ModuleResolver();
var mod = new ModuleDef(peReader, resolver);
var mdReader = mod.Reader;

foreach (var method in mod.GetDefinedMethods()) {
    try {
        var imp = new ILImporter(method);
        imp.ImportCode();
    } catch {
        Console.WriteLine("F: " + method);
    }
}

foreach (var method in mod.GetDefinedMethods()) {
    if (method.Name != "ObjAccess") continue;

    new DistIL.Passes.InlineMethods().Transform(method);
    new DistIL.Passes.MergeBlocks().Transform(method);
    new DistIL.Passes.SsaTransform2().Transform(method);
    new DistIL.Passes.DeadCodeElim().Transform(method);
    new DistIL.Passes.SimplifyConds().Transform(method);
    new DistIL.Passes.SimplifyArithm().Transform(method);
    new DistIL.Passes.ConstFold().Transform(method);
    new DistIL.Passes.DeadCodeElim().Transform(method);
    new DistIL.Passes.MergeBlocks().Transform(method);

    IRPrinter.ExportPlain(method, "../../logs/code.txt");
    IRPrinter.ExportDot(method, "../../logs/cfg.dot");

    new DistIL.Passes.RemovePhis().Transform(method);
    IRPrinter.ExportPlain(method, "../../logs/code_noPhis.txt");

    //new ILGenerator().EmitMethod(method);
}