# API Walkthrough
Note that this is mainly intended as an introduction for internal development, as many parts of the API are largely unpolished and may be changed without notice.

See also the [internals](./internals.md) document for details on inner-workings.

## Loading and saving modules
A module (or assembly) and all its dependencies must be loaded before IL-code can be manipulated. This is done through the `ModuleResolver` class:

```cs
var resolver = new ModuleResolver();
resolver.AddSearchPaths(new[] { Environment.CurrentDirectory }); //Search for modules in the current working directory
resolver.AddTrustedSearchPaths(); //Fallback to system modules from the current runtime
ModuleDef module = resolver.Load("Foo.dll");
// ...
module.Save("Foo_out.dll");
```

## Parsing and generating IL
Transforming IL directly is possible to some extent, after which it quickly becomes intractable. DistIL provides infrastructure for a [SSA-based intermediate representation](https://en.wikipedia.org/wiki/Static_single-assignment_form) similar to that of LLVM, in order to allow for complex analyses and transformations.

```cs
MethodDef method = module.MethodDefs().First(m => m.Name == "<Main>$"); //Find top-level program entry-point
MethodBody body = ILImporter.ParseCode(method); //Parse IL into a control flow graph
// ...
method.ILBody = ILGenerator.GenerateCode(body); //Generate IL code, taking care of register allocation and SSA-destruction
```

Note that SSA promotion and other passes require the setup of a `PassManager` (or at least a `MethodTransformContext`). See [DistIL.Cli's source](../src/DistIL.Cli/Program.cs).

## Dumping the IR
Taking easily readable snapshots of the IR is essential for development. DistIL supports a variety of flavors for IR dumps:

```cs
//Dump the IR in linear form to the console,
//using ANSI escape sequences for coloring
IRPrinter.ExportPlain(body, Console.Out);

//Dump the IR as a CFG to a Graphviz DOT file;
//There are several ways to render these:
// - Online sites such as http://magjac.com/graphviz-visual-editor
// - `Graphviz Interactive Preview` VSCode extension
// - Graphviz CLI
//This method also accepts a list of `IPrintDecorator` objects, which is 
//currently implemented by analyses such as `LivenessAnalysis` and `RegisterAllocator`.
IRPrinter.ExportDot(body, "logs/cfg.dot");

//Dump the IR in tree form to a text file
IRPrinter.ExportForest(body, "logs/forest.txt");
```

## Traversing blocks and instructions
`MethodBody` provides a simple helper for enumerating all instructions at once:

```cs
foreach (Instruction inst in body.Instructions()) {
    // ...
}
```

However, it may be the case where individual blocks need to be considered individually. This can be done using the default enumerators of `MethodBody` and `BasicBlock`:

```cs
foreach (BasicBlock block in body) {
    // ...
    foreach (Instruction inst in block) {  //add: block.Phis(), block.Guards(), block.NonPhis()
        // ...
    }
}
```

Blocks and instructions are stored in linked lists. It's generally okay to add or remove them inside an active loop because enumerators won't be invalidated.

The ordering is preserved from the source IL. The `MethodBody.TraverseDepthFirst()` method can be used to obtain pre and post depth-first block ordering.

## Basic pattern matching and rewriting
The basis for many transformations is pattern matching. For simple needs, C# support for pattern matching can be very useful (albeit not very scalable):

```cs
//Rewrite ((x + y) - y) -> x, for all int types
if (inst is BinaryInst { Op: BinaryOp.Sub, Left: BinaryInst { Op: BinaryOp.Add } lhs, Right: var y } && lhs.Right == y) {
    inst.ReplaceWith(lhs.Left); //Replace all uses of `inst` with `x`, then delete `inst` from the method.
}
```

## Generating complex IR
While it's possible to generate IR through constructors and manual insertion, such quickly becomes tedious. The `IRBuilder` class helps with the creation of complex sequences and control flow:

```cs
if (inst is CallInst { Method.Name: "WriteLine", Args: [ConstString { Value: "Hello, World!" }] } origCall) {
    //Note that the above will match calls to WriteLine() declared in any type.
    //More strict matching should check `Method.DeclaringType` against the imported TypeDesc.

    //Resolve used members
    TypeDefOrSpec t_Console = resolver.Import(typeof(Console));
    MethodDesc m_ReadLine = t_Console.FindMethod("ReadLine");
    MethodDesc m_Write = t_Console.FindMethod("Write", new MethodSig(PrimType.Void, new TypeSig[] { PrimType.String }));
    MethodDesc m_StrEqual = resolver.Import(typeof(string)).FindMethod("op_Equality");

    //Create a new empty block at the end of the method
    BasicBlock askKeyBlock = body.CreateBlock();

    //Split call block. Move all insts starting from `origCall` to a new block,
    //then change the old block branch to `askKeyBlock`
    BasicBlock newBlock = origCall.Block.Split(origCall, branchTo: askKeyBlock);

    //Populate `askKeyBlock`
    //  Console.Write("Enter key: ");
    //  goto Console.ReadLine() == "123" ? newBlock : askKeyBlock
    var builder = new IRBuilder(askKeyBlock, InsertionDir.After);
    builder.CreateCall(m_Write, ConstString.Create("Enter key: "));
    builder.SetBranch(
        builder.CreateCall(m_StrEqual, builder.CreateCall(m_ReadLine), ConstString.Create("123")),
        newBlock, askKeyBlock);
      
    //Rewrite existing call
    origCall.SetArg(0, ConstString.Create("Correct key! Bye, world."));
}
```
<img src="./images/callrewrite_irdump.png" height="230" />
<img src="./images/callrewrite_out.png" hspace="10"/>

_Generated IR and decompiled output_

## Traversing value use-chains
The use-chain of each `TrackedValue` is automatically updated whenever an instruction is created or removed. They are not keept for constants and type system entities.

The need to manually traverse use-chains is uncommon, however they are implicitly used to derive block successor/predecessor edges, and by helpers such as `TrackedValue.ReplaceUses()` and `Instruction.ReplaceWith()`.

```cs
//Check if all uses of an object allocation are from field load instructions
if (inst is NewObjInst && inst.Users().All(u => u is FieldLoadInst)) {
    // ...
}
```

## Using the pass manager
TODO