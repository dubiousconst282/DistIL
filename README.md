# DistIL
Optimization tool for .NET assemblies

TBD

# Design
DistIL uses a linear SSA IR, which is _heavily_ influenced by LLVM's IR.

_TBD_

Assembly reading and writing is done using DOM abstractions built on top of `System.Reflection.Metadata`.

# Implementation status
- [*] Assembly loading
  - [*] Type system (generics)
  - [*] DOM
- [*] IR
  - [*] Instructions
    - [x] ldc, ld/st loc/arg/elem/ind/fld
    - [*] Arithmetic (doesn't handle all types)
    - [x] Branches and compares
    - [x] Convert primitive number
    - [*] Call, callvirt, newobj (calli)
    - [*] ld loc/arg/elem/member address
    - [ ] localloc, cpblk, initblk, cpobj, initobj, sizeof
    - [*] newarray (castclass, isinst, ldtoken, box, unbox, mkrefany, refanytype, refanyval)
    - [ ] throw, rethrow, endfilter, endfinally, leave
    - [ ] arglist, jmp, ckfinite, tail., constrained.
    - [x] unaligned., volatile., no.*
  - [ ] Exception handlers (protected blocks)
- [*] Code generation (needs proper out-of-ssa, handle more instructions)
- [ ] Assembly writing
- [*] Good IR debugability (export cfg to graphviz or plain text)
- [ ] Better test coverage (if this becomes serious)
- [*] Analyses
  - [*] Dominance (post dom doesn't handle CFGs with no exits)
  - [x] Dominance frontier
- [*] Transforms
  - [*] SSA transform (doesn't handle address exposed variables)
  - [*] Out of SSA (only handles conventional ssa)
  - [*] Constant folding
    - [x] Basic arithmetic
    - [ ] Math.*, BitConverter.*, BitOperations.*, ...
  - [*] Function inlining (needs better heuristics, checks against private member access)
  - [ ] Linq inliner (needs: loop analysis?, delegate inlining)
  - [ ] Allocate objects on stack (needs: escape analysis?)
  - [*] Simplification/Peephole
  - [*] Dead code elimination
  - [ ] Common sub-expression elimination
  - [ ] Global value numbering
  - [ ] Loop unrolling and vectorization
  - [ ] Other less interesting transforms
  