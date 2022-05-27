# DistIL
Optimization and transformation framework for the Common Intermediate Language (WIP)

# Design/API
The input assembly and its dependencies are loaded using the `System.Reflection.Metadata` APIs, into mutable `ModuleDef` objects, which provides access to the defined types and other properties.
Special types (`Int32`, `String`, `Array`, and others) have singleton "references" defined in the `PrimType` class. The `GetDefinition()` method can be used to access the `System.Private.CoreLib` definition.

The intermediate representation (IR) consists of a control flow graph, where each node is a _basic block_ containing _instructions_ in static single assignment form. The IR design was influenced by LLVM.

Instructions are printed in the form of `[<result type> r<slot> =] <op> [operands]` (e.g. `int r1 = add #arg1, #arg2` or `call void Foo::Bar(int: #arg1)`). _Slots_ are sequential numbers which are lazily calculated for printing (they are not stored directly).

Local variables are accessed using `LoadVarInst`, `StoreVarInst`, and `VarAddrInst` (the later disables SSA _enregistration_).

Argument are accessed in the same way as local variables, until the SSA transform is applied. After that, they are treated as read only - loads are inlined into operands, and address exposed arguments are copied into local variables.

## Exceptions in the IR
Due to the large number of instructions that may potentially throw exceptions (and because it's easier for now), protected regions are represented implicitly in the CFG. Protected regions begins with a block starting with one or more `GuardInst` instructions, which indicates the handler/filter blocks. Exit blocks end with a single `LeaveInst` or `ContinueInst` (for filter/finally handlers) instruction.

Variables used across protected and normal regions are not _enregistered_/SSA-ified, because control flow may be interrupted at any point inside the protected region, thus later uses could have a wrong value.


## IR Examples
A simple example demonstrating a few basic optimizations:
<table>
  <tr>
    <td style="display: flex; gap: 4px;">
      <pre lang="csharp">
static int ObjAccess(int[] arr1, int[] arr2, int startIndex, int seed) {
  var bar = new Bar() { i = startIndex, seed = seed };
  while (bar.MoveNext(arr1)) {
    arr2[bar.j & 15] ^= bar.seed;
  }
  return bar.seed;
}</pre>
    </td>
    <td>
      <pre lang="csharp">
class Bar {
  public int i, j, seed;
  public bool MoveNext(int[] a) {
    if (i < 16) {
      j += a[i++] < 0 ? -1 : +1;
      seed = (seed * 8121 + 28411) % 134456;
      return true;
    }
    return false;
  }
}</pre>
    </td>
  </tr>
  <tr>
    <td>
      Unoptimized CFG:
      <img src="https://user-images.githubusercontent.com/87553666/170694612-56bd0a83-8539-4e01-943d-148d61e3ed9d.svg">
    </td>
    <td>
      Optimized CFG:
      <img src="https://user-images.githubusercontent.com/87553666/170694607-e321db19-9640-4332-acc0-3023c08971da.svg">
    </td>
  </tr>
</table>


An example demonstrating how exception handlers are represented:
<table>
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
            <img src="https://user-images.githubusercontent.com/87553666/170693986-71b25b61-985a-49bd-819e-29dd5aa55725.svg">
        </td>
    </tr>
</table>
