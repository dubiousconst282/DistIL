# DistIL
Experimental optimizer and intermediate representation for .NET CIL

Linq expansion is a key transformation currently done by DistIL. In conjunction with other optimizations, it is capable of promising results:

```cs
//Original code
public List<Thing> Linq11(List<string> src)
{
    return src
        .Select(s => new { Text = s, Tag = GetTag(s[0..8]) })
        .Where(s => GetCost(s.Text[0..8]) < 100)
        .Select(s => new Thing(s.Text, s.Tag))
        .ToList();
}
private string GetTag(string key) => _dict[key].Name;
private int GetCost(string key) => _dict[key].Cost;

//Decompiled output
public List<Thing> Linq11(List<string> src)
{
    //IgnoresAccessChecksToAttribute allows for unrestricted inlining and direct storage access (not possible in C#).
    int size = src._size;
    ref string reference = ref MemoryMarshal.GetArrayDataReference(src._items);
    ref string reference2 = ref Unsafe.Add(ref reference, (uint)size);
    List<Thing> list = new List<Thing>(size);
    for (; Unsafe.IsAddressLessThan(ref reference, ref reference2); reference = ref Unsafe.Add(ref reference, 1))
    {
        string text = reference;
        //Inlining enabled Value Numbering to reuse substring and dict lookup
        string key = text.Substring(0, 8);
        TagInfo tagInfo = _dict[key];
        //...and SROA to eliminate non-escaping annonymous object allocation.
        //Redundant copy is due to the lack of SSA rebuild, likely eliminated by JIT.
        string text2 = text;
        string <Name>k__BackingField = tagInfo.Name;
        if (tagInfo.Cost < 100)
        {
            list.Add(new Thing(text2, <Name>k__BackingField));
        }
    }
    return list;
}
```

There are many opportunities for other optimizations, spanning from replacing common BCL API calls with more efficient sequences, to complex transforms which may be too expansive or intricate to be performed by the JIT (loop opts, CFG simplifications, interprocedural analyses, and others).

DistIL is currently capable of successfully processing itself, and a few other libraries such as _ICSharpCode.Decompiler_ and _ImageSharp_, without observably breaking them.

# Notable features
- SSA-based Intermediate Representation
  - Renderable as plaintext and graphviz files
  - Parser for plaintext form
- Linq Expansion
- Lambda Devirtualization
- Method Inlining
- Scalar Replacement of Aggregates (object inlining)
- Value Numbering (*WIP)
- Register Allocation through graph coloring (unbounded)

Implementation notes and overview are [available here](docs/internals.md).

# Building / Usage
The optimizer is currently only accessible through a CLI, it can be run with the following (requires the [latest .NET SDK](https://dotnet.microsoft.com/en-us/download)):
```
git clone https://github.com/dubiousconst282/DistIL
cd DistIL
dotnet run --project src/DistIL.Cli -- -i "SourceModule.dll" -o "OptimizedModule.dll" -r "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\7.0.0"
```

Note that there has been very few tests and using the result in production is not recommended.

---

Pre-releases of the core library are available on [NuGet](https://www.nuget.org/packages/DistIL.Core). The sample below demonstrates basic interaction with it:
```cs
var resolver = new ModuleResolver();
resolver.AddTrustedSearchPaths(); //Use system modules from the current runtime
var module = resolver.Load("Foo.dll");

var method = module.AllMethods().First(m => m.Name == "Bar");
var body = ILImporter.ImportCode(method); //Parse CIL code into a CFG
//More setup required for SSA promotion and other passes, see DistIL.Cli's source.

foreach (var inst in body.Instructions()) {
    //Rewrite ((x + y) - y) -> x, for all int types
    if (inst is BinaryInst { Op: BinaryOp.Sub, Left: BinaryInst { Op: BinaryOp.Add } lhs, Right: var y } && lhs.Right == y) {
        inst.ReplaceWith(lhs.Left);
    }
}
IRPrinter.ExportPlain(body, Console.Out); //Alt. ExportDot() for a Graphviz file

method.ILBody = ILGenerator.Generate(body); //Generate CIL code from the CFG
module.Save("Output.dll");
```
<sub>(further documentation is currently scarse, if you have questions/requests please open an issue.)</sub>