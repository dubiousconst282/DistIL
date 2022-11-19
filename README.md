# DistIL
An experimental optimizer and compiler IR for .NET CIL

One of the primary targets is optimizations for well known libraries and usage patterns, which are simple to implement using pattern matching and in most cases don't require sophisticated analyses.  
A notable example is Linq expansion/inlining, which is currently implemented in just a few hundred lines of code (not including method/lambda inlining), and is capable of producing seemingly complex outputs:

<table>
  <tr> <th>Original code</th> <th>Decompiled output (braces removed for brevity)</th> </tr>
  <tr>    
    <td>
      <pre lang="csharp">
List&lt;int> Linq2(List&lt;object> src) {
  return src
    .OfType&lt;string>()
    .Where(s => s.Length > 0)
    .Select(s => int.Parse(s.Split(':')[0]))
    .ToList();
}
      </pre>
    </td>
    <td>
      <pre lang="csharp">
List&lt;int> Linq2(List&lt;object> src) {
  var list = new List&lt;int>(src.Count);
  for (int i = 0; i < src.Count; i++)
    if (src[i] is string text && text.Length > 0)
        list.Add(int.Parse(text.Split(':')[0]));
  return list;
}
      </pre>
    </td>
  </tr>
</table>

A few other analyses and transforms such as constant folding, method inlining, SSA construction and deconstruction are implemented as well, and are somewhat functional. Other optimizations such as object stack allocation/SROA and general loop optimizations are also planned for the near future (or at least fantasized of).

It is currently capable of sucessfully processing simple apps (come in and out of its IR without completely breaking them), but not yet much more.

# IR Overview
DistIL's _intermediate representation_ is based on a traditional _control flow graph_ with instructions in _static single assignment_ form, mainly inspired by LLVM. Most semantics are identical to CIL, since it is its main and only target.

Protected regions are by far the most complex aspect of the IR. They are represented as implicit sub-graphs of the main CFG, delimited by _guard_ and _leave_ instructions. Being implicit allows for a majority of transforms to work with none or few special cases, as guard/leave instructions provide clear boundaries for each region.  
Nevertheless, they bringing several complications such as additional constraints on SSA renaming, and requirement for proper block ordering during code generation.

The type system completely abstracts away things like entity handles/tokens and cross-assembly references, but it requires the entire dependency tree of a module to be available in order to work. It uses _System.Reflection.Metadata_ for module loading and writing.

## IR dumps
Being able to dump an IR into a readable form is a necessity for any compiler. DistIL can render its IR into both plaintext and Graphviz forms (with syntax highlighting).  
A parser for plaintext dumps is also implemented and only requires minimal changes such as adding type imports.

There are several ways to render Graphviz files, but [this VSCode extension](https://marketplace.visualstudio.com/items?itemName=tintinweb.graphviz-interactive-preview) seems to be the most convenient as it auto refreshes when the file changes, and provides decent zoom and dragging controls.

### Showcase: Linq expansion
<img src="https://user-images.githubusercontent.com/87553666/202864892-5f33647f-be40-43ac-b0b5-772e73663e7d.svg">

This CFG corresponds to the the Linq expansion sample in the heading.  
Most of the work involves inlining lambdas, the expansion transform itself only generates a simple loop and invokes the original lambdas. Another simplification pass detects and replaces them with direct calls, which can then be inlined further down.

### Showcase: Protected regions
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
      <img src="https://user-images.githubusercontent.com/87553666/202864893-1dc389bc-dde3-4937-8c05-36021d50c5ff.svg">
    </td>
  </tr>
</table>

SSA renaming is constrained for variables crossing protected regions, as exception control flow is implicit and phi instructions can only merge values at block boundaries.  
Information about individual regions is not keept in the CFG, but provided by a dedicated analysis (ProtectedRegionAnalysis), which identifies them using a recursive DFS and exposes the result as trees.

# Related projects
- https://github.com/jonathanvdc/Flame
- https://github.com/Washi1337/Echo
- https://github.com/edgardozoppi/analysis-net
