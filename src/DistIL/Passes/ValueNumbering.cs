namespace DistIL.Passes;

public class ValueNumbering : MethodPass
{
    public override void Run(MethodTransformContext ctx)
    {
        var map = new VNMap();

        foreach (var block in ctx.Method) {
            foreach (var inst in block) {
                if (s_Taggers.TryGetValue(inst.GetType(), out var tagger)) {
                    tagger.Update(map, inst);
                }
            }
            map.Pop();
        }
    }

    class VNMap
    {
        readonly Dictionary<Instruction, Value> _map = new(VNComparer.Instance);

        //Memoize inst result, or replace it with a memoized equivalent value.
        public void MemoizeOrReplace(Instruction inst)
        {
            if (_map.TryGetValue(inst, out var memoized)) {
                inst.ReplaceWith(memoized);
            } else {
                Replace(inst, inst);
            }
        }
        public void Replace(Instruction inst, Value value)
        {
            _map[inst] = value;
        }
        public void Invalidate(Instruction key)
        {
            _map.Remove(key);
        }

        public IEnumerable<Instruction> GetMemoizedAccesses()
        {
            //Load/Store insts are used interchangeably as location keys
            return _map.Keys.Where(k => k is LoadInst or StoreInst);
        }
        public IEnumerable<CallInst> GetMemoizedCalls()
        {
            return _map.Keys.OfType<CallInst>();
        }

        public void Push()
        {
            throw null!; //TODO: scoping for dominator based GVN
        }
        public void Pop()
        {
            _map.Clear();
        }
    }

    //Note: methods of this class will only be called for instructions with a registered tagger.
    class VNComparer : IEqualityComparer<Instruction>
    {
        public static readonly VNComparer Instance = new();

        public bool Equals(Instruction? x, Instruction? y)
        {
            var type = x!.GetType();
            if (y!.GetType() != type) {
                type = type.BaseType;
                //Both `x` and `y` must derive from some class, "SomethingAccessInst" 
                if (!typeof(AccessInst).IsAssignableFrom(type) || !typeof(AccessInst).IsAssignableFrom(y.GetType())) {
                    return false; //hashes collide
                }
                Debug.Assert(type != typeof(object) && type == y.GetType().BaseType);
            }
            return s_Taggers[type].Compare(x, y);
        }
        public int GetHashCode(Instruction inst)
        {
            return s_Taggers[inst.GetType()].Hash(inst);
        }
    }

    static readonly Dictionary<Type, InstTagger> s_Taggers = new();

    static ValueNumbering()
    {
        //value.Equals() must be used in some places becase e.g. Const values may have different instances.
        Reg<BinaryInst>(new() {
            CompareFn = (a, b) => a.Op == b.Op && a.Left.Equals(b.Left) && a.Right.Equals(b.Right),
            HashFn = (inst) => HashCode.Combine((int)inst.Op | 0x10000, inst.Left, inst.Right)
        });
        Reg<UnaryInst>(new() {
            CompareFn = (a, b) => a.Op == b.Op && a.Value.Equals(b.Value),
            HashFn = (inst) => HashCode.Combine((int)inst.Op | 0x20000, inst.Value)
        });
        Reg<CompareInst>(new() {
            CompareFn = (a, b) => a.Op == b.Op && a.Left.Equals(b.Left) && a.Right.Equals(b.Right),
            HashFn = (inst) => HashCode.Combine((int)inst.Op | 0x30000, inst.Left, inst.Right)
        });
        Reg<ConvertInst>(new() {
            CompareFn = (a, b) => a.ResultType == b.ResultType && a.Value.Equals(b.Value) && a.CheckOverflow == b.CheckOverflow && a.SrcUnsigned == b.SrcUnsigned,
            HashFn = (inst) => HashCode.Combine(inst.ResultType, inst.Value)
        });
        
        Reg<VarAddrInst>(new() {
            CompareFn = (a, b) => a.Var == b.Var,
            HashFn = (inst) => HashCode.Combine(inst.Var, 1234)
        });
        Reg<FieldAddrInst>(new() {
            CompareFn = (a, b) => a.Field == b.Field && a.Obj == b.Obj,
            HashFn = (inst) => HashCode.Combine(inst.Field, inst.Obj, 1234)
        });
        Reg<ArrayAddrInst>(new() {
            CompareFn = (a, b) => a.Array.Equals(b.Array) && a.Index.Equals(b.Index) && a.ElemType == b.ElemType && a.Flags == b.Flags,
            HashFn = (inst) => HashCode.Combine(inst.Array, inst.Index, 1234)
        });

        RegLoc<VarAccessInst, LoadVarInst, StoreVarInst>(new() {
            CompareFn = (a, b) => a.Var == b.Var,
            HashFn = (inst) => HashCode.Combine(inst.Var),
            //Variables can't alias, unless they're exposed
            MayAliasFn = (st, acc) => st.Var.IsExposed && acc is PtrAccessInst
        });
        RegLoc<PtrAccessInst, LoadPtrInst, StorePtrInst>(new() {
            CompareFn = (a, b) => false,//a.Address == b.Address && a.ElemType == b.ElemType && a.Flags == b.Flags,
            HashFn = (inst) => HashCode.Combine(inst.Address, inst.ElemType),
            MayAliasFn = (st, acc) => true
        });
        RegLoc<FieldAccessInst, LoadFieldInst, StoreFieldInst>(new() {
            CompareFn = (a, b) => a.Field.Equals(b.Field) && a.Obj == b.Obj,
            HashFn = (inst) => HashCode.Combine(inst.Field, inst.Obj),
            //Field stores may alias in some cases, e.g:
            //  Point& ptr = fldaddr Foo::wrapper
            //  int x1 = ldfld Point::X, ptr
            //  stfld Foo::wrapper, ...
            //  int x2 = ldfld Point::X, ptr  //different from x1
            MayAliasFn = (st, acc) => acc is PtrAccessInst or FieldAccessInst or VarAccessInst { Var.IsExposed: true }
        });
        RegLoc<ArrayAccessInst, LoadArrayInst, StoreArrayInst>(new() {
            CompareFn = (a, b) => a.Array.Equals(b.Array) && a.Index.Equals(b.Index) && a.ElemType == b.ElemType && a.Flags == b.Flags,
            HashFn = (inst) => HashCode.Combine(inst.Array, inst.Index),
            MayAliasFn = (st, acc) => acc is PtrAccessInst or ArrayAccessInst
        });
        s_Taggers.Add(typeof(CallInst), new CallTagger());
        //FIXME: tag invalidators for NewObj and Intrinsic insts

        void Reg<TInst>(LambdaTagger<TInst> tagger)
            where TInst : Instruction
        {
            s_Taggers.Add(typeof(TInst), tagger);
        }
        void RegLoc<TBase, TLoad, TStore>(LocationLambdaTagger<TBase, TLoad, TStore> tagger)
            where TBase : Instruction, AccessInst
            where TLoad : TBase, LoadInst
            where TStore : TBase, StoreInst
        {
            s_Taggers.Add(typeof(TBase), tagger);
            s_Taggers.Add(typeof(TLoad), tagger);
            s_Taggers.Add(typeof(TStore), tagger);
        }
    }

    abstract class InstTagger
    {
        public abstract bool Compare(Instruction a, Instruction b);
        public abstract int Hash(Instruction inst);

        public virtual void Update(VNMap map, Instruction inst)
        {
            map.MemoizeOrReplace(inst);
        }
    }
    class LambdaTagger<TInst> : InstTagger where TInst : Instruction
    {
        public Func<TInst, TInst, bool> CompareFn { get; init; } = null!;
        public Func<TInst, int> HashFn { get; init; } = null!;

        public override bool Compare(Instruction a, Instruction b)
            => CompareFn.Invoke((TInst)a, (TInst)b);

        public override int Hash(Instruction inst)
            => HashFn.Invoke((TInst)inst);
    }
    class LocationLambdaTagger<TBase, TLoad, TStore> : LambdaTagger<TBase>
        where TBase : Instruction, AccessInst
        where TLoad : TBase, LoadInst
        where TStore : TBase, StoreInst
    {
        //Checks if a store may alias with a memoized AccessInst
        public Func<TStore, Instruction, bool> MayAliasFn { get; init; } = null!;

        public override void Update(VNMap map, Instruction inst)
        {
            if (inst is TLoad) {
                map.MemoizeOrReplace(inst);
                return;
            }
            var store = (TStore)inst;

            foreach (var access in map.GetMemoizedAccesses()) {
                if (MayAliasFn.Invoke(store, access)) {
                    map.Invalidate(access);
                }
            }
            if (!store.IsCoerced) {
                map.Replace(inst, store.Value);
            } else {
                map.Invalidate(inst);
            }
        }
    }
    class CallTagger : InstTagger
    {
        //FIXME: proper equality for MethodSpec (maybe implement Equals() and GetHashCode()?)
        //       For now, reference comparation will work in most cases,
        //       since we don't always create new instances.
        public override bool Compare(Instruction a, Instruction b)
        {
            var callA = (CallInst)a;
            var callB = (CallInst)b;
            return callA.Method == callB.Method && callA.Args.SequenceEqual(callB.Args);
        }
        public override int Hash(Instruction inst)
        {
            var call = (CallInst)inst;
            return HashCode.Combine(call.Method, call.NumArgs > 0 ? call.Args[0] : null);
        }

        public override void Update(VNMap map, Instruction inst)
        {
            var call = (CallInst)inst;
            switch (GetKind(call.Method)) {
                case FuncKind.Pure or FuncKind.ObjAccessor: {
                    map.MemoizeOrReplace(call);
                    break;
                }
                case FuncKind.ObjMutator: {
                    foreach (var otherCall in map.GetMemoizedCalls()) {
                        if (GetKind(otherCall.Method) == FuncKind.ObjAccessor) {
                            map.Invalidate(otherCall);
                        }
                    }
                    break;
                }
                case FuncKind.Unknown: {
                    foreach (var access in map.GetMemoizedAccesses()) {
                        if (MayAffectAccess(call, access)) {
                            map.Invalidate(access);
                        }
                    }
                    break;
                }
            }
        }

        private static bool MayAffectAccess(CallInst call, Instruction acc)
        {
            //Local vars can't be affected by a call, unless they're exposed (and the method takes some pointer or ref).
            return acc is not VarAccessInst { Var.IsExposed: false };
        }

        private static FuncKind GetKind(MethodDesc fn)
        {
            if (fn.DeclaringType is TypeDefOrSpec type && type.Module == type.Module.Resolver.CoreLib) {
                if (IsPure(type.Namespace, type.Name, fn)) {
                    return FuncKind.Pure;
                }
                if (type.Namespace == "System.Collections.Generic" && type.Name is "Dictionary`2" or "List`1" or "HashSet`1") {
                    bool isAccessor = fn.Name is "get_Item" or "ContainsKey";
                    return isAccessor ? FuncKind.ObjAccessor : FuncKind.ObjMutator;
                }
            }
            return FuncKind.Unknown;
        }

        private static bool IsPure(string? ns, string typeName, MethodDesc fn)
        {
            if (ns == "System") {
                if (typeName is "Math" or "MathF") {
                    return true;
                }
                if (typeName is "Int32" or "Int64" or "Single" or "Double" or "DateTime" or "Decimal") {
                    //ToString() et al. aren't pure because they depend on the instance reference.
                    return fn.Name is "Parse" or "TryParse" && fn.ParamSig[0] == PrimType.String;
                }
                if (typeName is "String") {
                    return fn.Name is
                        "Compare" or "CompareTo" or "Substring" or "Concat" or
                        "Replace" or "Contains" or "IndexOf" or "LastIndexOf" or
                        "ToLower" or "ToUpper" or
                        "op_Equality" or "op_Inequality" or "Equals";
                }
            } else if (ns == "System.Numerics" && typeName == "BitOperations") {
                return true;
            }
            return false;
        }

        enum FuncKind
        {
            Unknown,        //Can't be VN
            Pure,           //Returns the same input for a given set of arguments, without mutating memory (may still throw or cause other side effects).
            ObjAccessor,    //Pure within an object instance (first argument).
            ObjMutator      //Mutates an instance object, but not global memory.
        }
    }
}