# Internals

## The IR
The method _intermediate representation_ is based on a traditional _control flow graph_ with instructions in _static single assignment_ form. Many ideas and implementation details were taken from LLVM, but all core semantics are inherited from CIL. Almost every IR instruction directly corresponds to one or more CIL opcodes.

For simplicity, the _type system_ is tightly coupled to the method IR. It was designed to abstract away things like tokens and _reference_ entities, the later requires the entire dependency tree of a module to be available in order to work.  
The current implementation uses _System.Reflection.Metadata_ as frontend, but bridges between different loaders such as _Cecil_, _dnlib_, _AsmResolver_, and others could be implemented with relatively little effort.

## Linq expansion
Though it may be perfectly possible for Linq queries (and extensions) to be optimized at a great extent by general transforms such as devirtualization, inlining, SROA/object stack allocation, and further CFG simplification, these would likely need significant tunning to work effectively. A pattern matching transform is considerably easier to implement and gives much more control over the final code, but at the inherent cost of being tied to specific library calls.

The current implementation identifies all queries in a method, before generating code by traversing the _pipeline_ from start to end.  
This traversal order allows for more flexibility on each stage by eliminating the need of state machinery that would otherwise be required to emulate enumeration via the _MoveNext()_ and _get_Current()_ methods. An example of this is the _SelectMany()_ stage, it can trivially generate an inner loop with this model.  
Lambda invocations are replaced with direct calls by another simplification pass, allowing them to be inlined further down.


<img src="images/linq_opt.png">

_Example optimized CFG of a simple Linq query, previously having an anonymous object allocation._

## Protected regions

<table>
  <tr> <th>Original code</th> <th>CFG</th> </tr>
  <tr>
    <td>
      <pre lang="csharp">
int Try2(string str) {
  int r = 0;
  try {
    r = str.Length > 0 
      ? int.Parse(str) : 0;
    r *= 5;
  } catch (FormatException ex) {
    Console.WriteLine(ex);
    r = -1;
  } finally {
    r += 30;
  }
  return r;
}
      </pre>
    </td>
    <td>
      <img src="images/eh_regions.png">
    </td>
  </tr>
</table>

Protected regions are by far the most complex aspect of the IR. They are represented as implicit sub-graphs of the main CFG, delimited by _guard_ and _leave_ instructions. Being implicit allows for many transforms to work with none or few special cases, as guard/leave instructions provide clear region boundaries without introducing indirection levels or changing the overall shape of the IR.

Exception control flow is implicit, instructions may throw exceptions anywhere inside a basic block. SSA renaming is currently constrained for variables assigned inside regions, because execution could be interrupted before a definition reaches a phi instruction, the original meaning of the program would be lost.

## Code generation
The generator emits CIL code by recursively traversing expression trees formed in the IR, while assigning result values into variables assigned by the register allocator.

Building deep expression trees is important not only for code size, but also because small trees could hinder RyuJIT's ability to match patterns and perform its own optimizations, due to its use of a tree-based IR (though this may be less problematic with its new _forward substitution_ pass).  
Trees are implicitly formed from the SSA-graph by assigning each definition a _leaf_ flag, indicating whether they are a sub-expression (implying it has a single use), or the root of a tree (implying it must be emitted and its result value be stored into a temp variable or discarded).

The _register allocator_ also helps translating out of SSA form by aggressively coalescing phi arguments back into a single variable, and scheduling _parallel copies_ for arguments that cannot be coalesced. These copies are sequentialized by the generator using normal load/stores just before branches are emitted.

In some cases, the resulting code may not be optimal because there's little tunning on existing transforms, most notably _value numbering_ and _coalescing_. They present the well known problem of increasing register pressure, causing the "optimized" code to endup with more spills than the original due to more variables being alive at one point.
