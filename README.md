# DistIL
Experimental optimizer and compiler IR for .NET CIL

One of the primary targets is optimizations for well known libraries and usage patterns, most of which are simple to implement using pattern matching and often don't require other analyses.  
A notable example is Linq expansion/inlining, which is currently implemented in just a few hundred lines of code (not including method/lambda inlining), and is capable of producing seemingly complex results:

<table>
  <tr> <th>Original code</th> <th>Decompiled output (reformatted for brevity)</th> </tr>
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

Another intriguing transform is an extension to value numbering, which is aware of state across calls to well known instance methods, and thus able to eliminate duplicate calls for some of them:

<table>
  <tr> <th>Original code</th> <th>Decompiled output</th> </tr>
  <tr>    
    <td>
      <pre lang="csharp">
string GetInfo(string key)
  => GetName(key) + " #" + GetCode(key);
//_things and _codes are Dictionary fields
string GetName(string key)
  => _things[key].Name.Substring(0, 8);
int GetCode(string key)
  => _codes[GetName(key)];
      </pre>
    </td>
    <td>
      <pre lang="csharp">
public string GetInfo(string key) {
    //After both Get() calls are inlined, VN will
    //see and reuse the dictionary lookup calls:
    string text = _things[key].Name.Substring(0, 8);
    return text + " #" + _codes[text];
}
//...
      </pre>
    </td>
  </tr>
</table>

A few other analyses and transforms such as constant folding, method inlining, SSA construction and deconstruction are implemented as well. More sophisticated optimizations such as object stack allocation/SROA and general loop optimizations are also planned for the near future (or at least fantasized of).

DistIL is currently capable of sucessfully processing itself, and a few other libraries such as _ICSharpCode.Decompiler_ and _ImageSharp_, without observably breaking them.

# IR overview
DistIL's _intermediate representation_ is based on a traditional _control flow graph_ with instructions in _static single assignment_ form, mainly inspired by LLVM. It tries to be simple and stay as close to CIL as possible, since it is its main and only target.

One of the most complex aspects of the IR are protected regions. They are represented as implicit sub-graphs of the main CFG, delimited by _guard_ and _leave_ instructions. Being implicit allows for a majority of transforms dealing with control flow to work with none or few special cases, as guard/leave instructions provide clear boundaries for each region.  
Nevertheless, they bringing several complications such as additional constraints on SSA renaming, and requirement for proper block ordering during code generation.

The type system completely abstracts away things like entity handles/tokens and cross-assembly references, but it requires the entire dependency tree of a module to be available in order to work. It uses _System.Reflection.Metadata_ for module loading and writing.

## IR dumps
Being able to dump an IR into a readable form is a necessity for any compiler. DistIL can render its IR into both plaintext and Graphviz forms (with syntax highlighting).  
A parser for plaintext dumps is partially implemented and only requires minimal changes such as adding type imports.

### Showcase: Linq expansion
<img src="https://user-images.githubusercontent.com/87553666/202864892-5f33647f-be40-43ac-b0b5-772e73663e7d.svg">

This CFG corresponds to the the optimized Linq expansion sample in the heading.  
Most of the work involves inlining lambdas, the expansion transform itself only generates a simple loop and invokes the original lambdas. Another simplification pass detects and replaces them with direct calls, which may then be inlined further down.

The transform does currently cause subtle behavior changes, for example, concurrent modification checking done by `List<T>.Enumerator` and the lambda instance cache are eliminated. In the future, transform "safety" settings could be implemented in order to preserve them.

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

SSA renaming is constrained for variables crossing protected regions, because exception control flow is implicit and phi instructions can only merge values at block boundaries.  
Information about individual regions is not keept in the CFG, but provided by a dedicated analysis (ProtectedRegionAnalysis), which identifies them using a recursive DFS and exposes the result as trees.

# Related projects
- https://github.com/jonathanvdc/Flame
- https://github.com/Washi1337/Echo
- https://github.com/edgardozoppi/analysis-net
