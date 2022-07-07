# DistIL
Optimization and transformation framework for the Common Intermediate Language (WIP)

---

Currently, the most notable implemented features are:
- Type system and module loader/writer based on System.Reflection.Metadata
- Simple and consistent SSA based Intermediate Representation
- Analyses: Dominator tree, Liveness
- Optimizations:
  - Method Inlining, Constant Folding, Dead Code Elimination, Local Value Numbering
  - Linq Query Expansion: Convert reification/reduction queries (ToArray, Count, ...) into imperative code
  - Delegate Concretization: Replace delegates invocations with direct calls (if they are known)

# IR Details
DistIL uses a SSA-based intermediate representation, which focus on being simple and suitable for general program optimizations. It is in fact very similar to LLVM's IR, the obvious difference being that it is based on CIL, so it has direct support for things like objects, fields, and such.

## Variables
Local variables are treated similarly as memory locations, they are accessed using the `LoadVarInst` and `StoreVarInst` instructions. They can also have their address exposed by `VarAddrInst`, but this operation prevents it from being transformed into an SSA _register_. They are currently not explicitly declared nor tracked in the IR.

Arguments are always read only. The CIL importer (parser), copies them into local variables at the entry block, most of these copies are removed by the SSA transform pass.

## Exception Regions
Due to the large number of instructions that may potentially throw exceptions, exception handlers are represented implicitly on the CFG (blocks don't end at instructions that may throw). Protected regions start with a single entry block, which contains one or more `GuardInst` instructions, each of which point to the handler/filter blocks. Exit blocks end with a single `LeaveInst` or `ContinueInst` (for filter/finally handlers) instruction.

Variables used across protected and normal regions are marked as exposed (to disable SSA registration), because control flow may be interrupted at any point inside the protected region, and phi instructions can only merge values at block edges.

## Code Generation
Once all phi instructions have been removed (we currently use the technique described in "Revisiting Out-of-SSA Translation" by Boissinot et al.), generating CIL code is quite straightforward.

Because the IR forms a kind of DAG structure, we simply visit each instruction and recurse into operands if they can be inlined to form an deeper expression. An instruction can be inlined if it only has one use within the same block, and there are no interferences to its operands before the result is used. Otherwise, it must be placed into into a temporary variable.

There's no register allocation pass yet, and while the result code is decent, it might have many temporary variables.

## IR Dumps
The IR can be easily dumped in plain text or graphviz forms, some examples are shown later in this section.

In the text form of the IR, instructions are automatically assigned a _temporary variable_/_register_ for its result, they doesn't actually exist in the IR - the instruction itself _is_ the register, operands just refer to them directly.

Blocks, instructions and variables are given sequential names by the `SymbolTable` class (e.g. `BB_01, BB_02, ...; r1, r2, ...`), but custom names can also be set. The `Namify` pass can be used to generate fixed names, which can be useful when generating diffs between passes.

---

Exception handlers:
<table>
  <tr> <th>Original code</th> <th>CFG</th> </tr>
  <tr>
    <td>
      <pre lang="csharp">
static int Try2(string str) {
  int r = 0;
  try {
    int tmp = str.Length == 0 ? 1 : int.Parse(str);
    r = tmp;
    r *= 2;
  } catch (FormatException ex) {
    Console.WriteLine(ex);
    r += 2;
  } finally {
    r += 3;
  }
  return r;
}
      </pre>
    </td>
    <td>
      <img src="https://user-images.githubusercontent.com/87553666/177763432-746541a7-9074-4f7c-aff9-c070cd4e6598.svg">
    </td>
  </tr>
</table>

---
Linq expansion + delegate concretization + inlining:
<table>
  <tr> <th>Original code</th> <th>Optimized CFG</th> </tr>
  <tr>
    <td>
      <pre lang="csharp">
static int[] Linq1(int[] arr) {
  return arr.Where(x => x > 0)
            .Select(x => x * 2)
            .ToArray();
}
      </pre>
    </td>
    <td>
      <img src="https://user-images.githubusercontent.com/87553666/177763428-ab1b772e-145b-45a7-bc8e-4cb9c9f79a47.svg">
    </td>
  </tr>
</table>

<details>
  <summary>Decompiled output</summary>
Not looking pretty because the out of SSA pass isn't fully done yet. It will get there eventually :)

```cs
int[] Linq1(int[] arr) {
    int num = arr.Length;
    int[] array = new int[num];
    int num2 = 0;
    int num3 = 0;
    int num4;
    while (true) {
        num4 = num3;
        if (num2 >= num) break;
        
        int num5 = arr[num2];
        num3 = num4;
        if (num5 > 0) {
            array[num4] = num5 * 2;
            num3 = num4 + 1;
        }
        num2++;
    }
    int num6 = ((num4 != num) ? 1 : 0);
    int[] array2 = array;
    if (num6 != 0) {
        array2 = new int[num4];
        Array.Copy(array, array2, num4);
    }
    return array2;
}
```
</details>
