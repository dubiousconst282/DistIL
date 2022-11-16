using System.Collections.Immutable;
using System.Reflection;
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
resolver.AddTrustedSearchPaths();
resolver.AddSearchPaths(new[] { Path.GetDirectoryName(args[0])! });
var module = resolver.Load(args[0]);

var mp1 = new MethodPassManager();
mp1.Add(new SimplifyCFG());
mp1.Add(new SsaTransform());

var mp2 = new MethodPassManager();
mp2.Add(new ExpandLinq(module));
mp2.Add(new SimplifyInsts(module)); //lambdas and devirtualization
mp2.Add(new InlineMethods());
mp2.Add(new SimplifyInsts(module));
//mp2.Add(new LoopInvariantCodeMotion());
mp2.Add(new DeadCodeElim());
mp2.Add(new SimplifyCFG());
//mp2.Add(new ValueNumbering());

if (args.Length >= 3) {
    mp2.Add(new DumpPass() {
        BaseDir = args[2], 
        Filter = args.Length >= 4 ? args[3] : null
    });
}

var mp3 = new MethodPassManager();
mp3.Add(new RemovePhis());

var modPm = new ModulePassManager();
modPm.Add(new ImportPass());
modPm.Add(mp1);
modPm.Add(mp2);
modPm.Add(mp3);
modPm.Add(new ExportPass());

modPm.Run(module);

var ignoreAccChecksAttrib = module.CreateType(
    "System.Runtime.CompilerServices", "IgnoresAccessChecksToAttribute", 
    TypeAttributes.BeforeFieldInit | TypeAttributes.Class,
    resolver.CoreLib.FindType("System", "Attribute")
);
var ignoreAccChecksCtor = ignoreAccChecksAttrib.CreateMethod(
    ".ctor", PrimType.Void, 
    ImmutableArray.Create(new ParamDef(ignoreAccChecksAttrib, "this"), new ParamDef(PrimType.String, "assemblyName")), 
    MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig
);
ignoreAccChecksCtor.ILBody = new ILMethodBody() {
    Instructions = new[] { new ILInstruction(ILCode.Ret) }
};
var asmCAs = module.GetCustomAttribs(forAssembly: true);
asmCAs.Add(new CustomAttrib(ignoreAccChecksCtor, ImmutableArray.Create<object?>(module.AsmName.Name)));

if (args.Length >= 2) {
    using var outStream = File.Create(args[1]);
    module.Save(outStream);
}

class DumpPass : MethodPass
{
    public string BaseDir { get; init; } = null!;
    public string? Filter { get; init; }

    public override void Run(MethodTransformContext ctx)
    {
        var diags = IRVerifier.Diagnose(ctx.Method);
        if (diags.Count > 0) {
            Console.WriteLine($"BadIR in {ctx.Method}:\n  {string.Join("\n  ", diags)}");
        }
        var def = ctx.Method.Definition;
        string name = $"{def.DeclaringType.Name}::{def.Name}";
        if (Filter == null || Regex.IsMatch(name, Filter)) {
            //Escape all Windows forbidden characters to prevent issues with NTFS partitions on Linux
            name = Regex.Replace(name, @"[\x00-\x1F:*?\/\\""<>|]", "_");

            Directory.CreateDirectory(BaseDir);
            
            IRPrinter.ExportPlain(ctx.Method, $"{BaseDir}/{name}.txt");
            IRPrinter.ExportDot(ctx.Method, $"{BaseDir}/{name}.dot");
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
                method.Body = ILImporter.ImportCode(method);
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
                method.ILBody = ILGenerator.Generate(method.Body);
            } catch (Exception ex) {
                Console.WriteLine($"FailEmit: {method} {ex.Message}");
            }
        }
    }
}