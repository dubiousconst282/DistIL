namespace DistIL.Passes;

public class ValueNumbering : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var map = new VNMap();

        foreach (var block in ctx.Method) {
            foreach (var inst in block) {
                if (s_Taggers.TryGetValue(inst.GetType(), out var tagger)) {
                    tagger.Update(map, inst);
                }
            }
            map.Clear();
        }
        return map.MadeChanges ? MethodInvalidations.DataFlow : 0;
    }

    class VNMap
    {
        readonly Dictionary<Instruction, Value> _map = new(VNComparer.Instance);
        public bool MadeChanges = false;

        //Memoize inst result, or replace it with a memoized equivalent value.
        public void MemoizeOrReplace(Instruction inst)
        {
            if (_map.TryGetValue(inst, out var memoized)) {
                inst.ReplaceWith(memoized);
                MadeChanges = true;
            } else {
                Replace(inst, inst);
            }
        }
        public void Replace(Instruction inst, Value value)
        {
            _map[inst] = value;
        }
        public void Invalidate(Instruction inst)
        {
            _map.Remove(inst);
        }
        
        public void InvalidateAccesses<TInst>(TInst inst, Func<TInst, MemoryInst, bool> mayAlias)
        {
            //Load/Store insts are used interchangeably as location keys
            foreach (var access in _map.Keys.OfType<MemoryInst>()) {
                if (mayAlias(inst, access)) {
                    Invalidate(access);
                }
            }
        }
        public void InvalidateCalls(Func<CallInst, bool> mayAlias)
        {
            //Load/Store insts are used interchangeably as location keys
            foreach (var call in _map.Keys.OfType<CallInst>()) {
                if (mayAlias(call)) {
                    Invalidate(call);
                }
            }
        }

        public void Clear()
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

            //Load/store instructions can have matching tags.
            if (y!.GetType() != type) {
                if (!(x is MemoryInst && y is MemoryInst)) {
                    return false;
                }
                type = typeof(LoadInst);
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
        
        Reg<FieldAddrInst>(new() {
            CompareFn = (a, b) => a.Field == b.Field && a.Obj == b.Obj,
            HashFn = (inst) => HashCode.Combine(inst.Field, inst.Obj, 1234)
        });
        Reg<ArrayAddrInst>(new() {
            CompareFn = (a, b) => a.Array.Equals(b.Array) && a.Index.Equals(b.Index) && a.ElemType == b.ElemType && a.InBounds == b.InBounds && a.IsReadOnly == b.IsReadOnly,
            HashFn = (inst) => HashCode.Combine(inst.Array, inst.Index, 1234)
        });
        Reg<PtrOffsetInst>(new() {
            CompareFn = (a, b) => a.BasePtr == b.BasePtr && a.Index.Equals(b.Index) && (a.KnownStride ? a.Stride == b.Stride : a.ElemType == b.ElemType),
            HashFn = (inst) => HashCode.Combine(inst.BasePtr, inst.Index, inst.Stride)
        });

        var memTagger = new MemoryTagger();
        s_Taggers.Add(typeof(LoadInst), memTagger);
        s_Taggers.Add(typeof(StoreInst), memTagger);

        s_Taggers.Add(typeof(CallInst), new CallTagger());
        //FIXME: tag invalidators for NewObj and Intrinsic insts

        static void Reg<TInst>(LambdaTagger<TInst> tagger)
            where TInst : Instruction
        {
            s_Taggers.Add(typeof(TInst), tagger);
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
    class MemoryTagger : InstTagger
    {
        public override bool Compare(Instruction a, Instruction b)
        {
            var ma = (MemoryInst)a;
            var mb = (MemoryInst)b;
            return ma.Address == mb.Address && ma.ElemType == mb.ElemType;
        }
        public override int Hash(Instruction inst)
        {
            return HashCode.Combine(((MemoryInst)inst).Address);
        }

        public override void Update(VNMap map, Instruction inst)
        {
            if (inst is LoadInst) {
                map.MemoizeOrReplace(inst);
                return;
            }
            var store = (StoreInst)inst;
            map.InvalidateAccesses(store, (store, otherAcc) => MayAlias(store.Address, otherAcc.Address));

            if (!StoreInst.MustBeCoerced(store.ElemType, store.Value)) {
                map.Replace(store, store.Value);
            }
        }

        private static bool MayAlias(Value addr1, Value addr2)
        {
            return (addr1, addr2) switch {
                //Different fields will never alias unless they're have explicit layout struct; but we don't care for now
                (FieldAddrInst f1, FieldAddrInst f2) => f1.Field == f2.Field,
                _ => true //assume the worst
            };
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
                    map.InvalidateCalls((otherCall) => GetKind(otherCall.Method) == FuncKind.ObjAccessor);
                    break;
                }
                case FuncKind.Unknown: {
                    map.InvalidateAccesses(call, MayAffectAccess);
                    break;
                }
            }
        }

        private static bool MayAffectAccess(CallInst call, MemoryInst acc)
        {
            //Local vars can't be affected by a call unless they're exposed
            return acc.Address is not LocalSlot slot || slot.IsExposed();
        }

        private static FuncKind GetKind(MethodDesc fn)
        {
            if (fn.DeclaringType is TypeDefOrSpec type && type.IsCorelibType()) {
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