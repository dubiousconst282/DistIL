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

