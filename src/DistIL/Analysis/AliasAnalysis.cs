namespace DistIL.Analysis;

public class AliasAnalysis : IMethodAnalysis
{
    public AliasAnalysis(MethodBody method) { }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new AliasAnalysis(mgr.Method);

    /// <summary> Checks if two given symbolic values may have the same value at runtime. </summary>
    public bool MayAlias(Value v1, Value v2)
    {
        if (object.ReferenceEquals(v1, v2)) {
            return true;
        }
        if (!MayTypesAlias(v1.ResultType, v2.ResultType)) {
            return false;
        }

        static int AssocRank(Value val) => val switch {
            LocalSlot => 10,
            FieldAddrInst => 20,
            ArrayAddrInst => 30,
            PtrOffsetInst => 40,
            _ => 100
        };

        if (AssocRank(v1) > AssocRank(v2)) {
            (v1, v2) = (v2, v1);
        }

        switch (v1, v2) {
            case (FieldAddrInst f1, FieldAddrInst f2): {
                if (f1.Field == f2.Field) {
                    return f1.IsStatic || MayAlias(f1.Obj, f2.Obj!);
                }
                // Different fields will never alias, unless they're in an struct with explicit layout
                var field1 = ((FieldDefOrSpec)f1.Field).Definition;
                var field2 = ((FieldDefOrSpec)f2.Field).Definition;
                return field1.DeclaringType == field2.DeclaringType && field1.HasLayoutOffset;
            }
            case (FieldAddrInst f1, ArrayAddrInst a1): {
                return f1.IsInstance && f1.Field.DeclaringType.IsValueType && 
                       a1.ElemType.IsValueType && MayAlias(f1.Obj, a1.Array);
            }
            case (LocalSlot l1, FieldAddrInst f1): {
                return f1.IsInstance && f1.Field.DeclaringType.IsValueType && MayAlias(l1, f1.Obj);
            }
            case (ArrayAddrInst a1, ArrayAddrInst a2) : {
                return MayAlias(a1.Array, a2.Array) && MayAlias(a1.Index, a2.Index);
            }
            case (LocalSlot l1, LocalSlot l2): {
                return l1 == l2;
            }
            case (LocalSlot, ArrayAddrInst or Argument or Const): {
                return false;
            }
            case (Const, Const): {
                return v1.Equals(v2);
            }
        }
        return true;
    }

    /// Checks if two objects/pointers of the given types can be aliased.
    private bool MayTypesAlias(TypeDesc t1, TypeDesc t2)
    {
        if (!object.ReferenceEquals(t1, t2)) {
            // Assume that pointers always alias unless one points to a managed object and the other doesn't.
            if (t1 is PointerType && t2 is PointerType) {
                return t1.ElemType!.IsManagedObject() == t2.ElemType!.IsManagedObject();
            }
            if (t1 is ArrayType && t2 is ArrayType) {
                var e1 = t1.ElemType!;
                var e2 = t2.ElemType!;

                // U[] and S[] may alias (e.g. int[] and uint[])
                if (e1.Kind.IsInt() || e2.Kind.IsInt()) {
                    return e1.Kind.GetSigned() == e2.Kind.GetSigned();
                }
                // A[] and B[] may alias if both are classes and one inherits from the other.
                // otherwise, T[] may alias with T[]
                return MayObjTypesAlias(e1, e2) ?? (t1 == e2);
            }
            return MayObjTypesAlias(t1, t2) ?? true;
        }
        return true;
    }

    // Checks if two objects of the given types may alias. If both aren't object types, returns null.
    private bool? MayObjTypesAlias(TypeDesc t1, TypeDesc t2)
    {
        bool isObj1 = t1.IsManagedObject();
        bool isObj2 = t2.IsManagedObject();

        if (isObj1 || isObj2) {
            if (isObj1 && isObj2) {
                return t1.Inherits(t2) || t2.Inherits(t1);
            }
            return false;
        }
        return null;
    }
}
