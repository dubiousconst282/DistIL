namespace DistIL.AsmIO;

using System.Collections;
using System.Reflection;

/// <summary> Represents a single dimensional array type. </summary>
public class ArrayType : CompoundType
{
    public override TypeKind Kind => TypeKind.Array;
    public override StackType StackType => StackType.Object;
    public override TypeDesc? BaseType => PrimType.Array;

    protected override string Postfix => "[]";

    internal ArrayType(TypeDesc elemType)
        : base(elemType) { }

    internal bool Implements(TypeDefOrSpec itf)
    {
        return itf.IsCorelibType() && itf.Namespace switch {
            "System"
                => itf.Name == "ICloneable",
            "System.Collections"
                => Array.IndexOf(s_BoxedCollectionNames, itf.Name) >= 0,
            "System.Collections.Generic"
                => Array.IndexOf(s_GenCollectionNames, itf.Name) >= 0 && 
                   ElemType.IsAssignableTo(itf.GenericParams[0]),
            _ => false
        };
    }

    static readonly string[] s_GenCollectionNames = {
        "IList`1", "ICollection`1", "IEnumerable`1",
        "IReadOnlyList`1", "IReadOnlyCollection`1"
    };
    static readonly string[] s_BoxedCollectionNames = {
        "IList", "ICollection", "IEnumerable",
        "IStructuralComparable", "IStructuralEquatable"
    };

    protected override CompoundType New(TypeDesc elemType)
        => new ArrayType(elemType);
}

/// <summary> Represents an overly complicated multi-dimensional array type. </summary>
public class MDArrayType : CompoundType
{
    public int Rank { get; }
    public ImmutableArray<int> LowerBounds { get; }
    public ImmutableArray<int> Sizes { get; }

    public override TypeKind Kind => TypeKind.Array;
    public override StackType StackType => StackType.Object;
    public override TypeDesc? BaseType => PrimType.Array;

    private List<MDArrayMethod>? _methods;
    public override IReadOnlyList<MDArrayMethod> Methods {
        get {
            if (_methods == null) {
                int count = (int)MDArrayMethod.OpKind.Count_;
                _methods = new List<MDArrayMethod>(count);
                for (int i = 0; i < count; i++) {
                    _methods.Add(new MDArrayMethod(this, (MDArrayMethod.OpKind)i));
                }
            }
            return _methods;
        }
    }
    protected override string Postfix {
        get {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < Rank; i++) {
                if (i != 0) sb.Append(',');

                int lowerBound = 0;

                if (i < LowerBounds.Length) {
                    lowerBound = LowerBounds[i];
                    sb.Append(lowerBound);
                }
                sb.Append("...");

                if (i < Sizes.Length) {
                    sb.Append(lowerBound + Sizes[i] - 1);
                }
            }
            sb.Append(']');
            return sb.ToString();
        }
    }

    public MDArrayType(TypeDesc elemType, int rank, ImmutableArray<int> lowerBounds, ImmutableArray<int> sizes)
        : base(elemType)
    {
        Rank = rank;
        LowerBounds = lowerBounds;
        Sizes = sizes;
    }

    protected override CompoundType New(TypeDesc elemType)
        => new MDArrayType(elemType, Rank, LowerBounds, Sizes);

    public override bool Equals(TypeDesc? other)
        => other is MDArrayType o && o.ElemType.Equals(ElemType) && o.Rank == Rank && 
           o.Sizes.SequenceEqual(Sizes) && o.LowerBounds.SequenceEqual(LowerBounds);
}
/// <summary> Represents a multi-dimensional array VES intrinsic (II.14.2) </summary>
public class MDArrayMethod : MethodDesc
{
    public enum OpKind
    {
        SizeCtor,   //void .ctor(this, int len1, int len2, ...)
        RangeCtor,  //void .ctor(this, int lo1, int hi1, int lo2, int hi2, ...)
        Set,        //void   Set(this, int idx1, int idx2, T value)
        Get,        //T      Get(this, int idx1, int idx2, ...)
        Address,    //T& Address(this, int idx1, int idx2, ...)
        Count_
    }

    public override MDArrayType DeclaringType { get; }
    public OpKind Kind { get; }

    public override string Name
        => Kind <= OpKind.RangeCtor ? ".ctor" : Kind.ToString();

    public override MethodAttributes Attribs
        => MethodAttributes.Public | (Kind <= OpKind.RangeCtor ? MethodAttributes.SpecialName : 0);

    public override MethodImplAttributes ImplAttribs
        => MethodImplAttributes.InternalCall;

    public override TypeSig ReturnSig => Kind switch {
        <= OpKind.Set  => PrimType.Void,
        OpKind.Get     => DeclaringType.ElemType,
        OpKind.Address => DeclaringType.ElemType.CreateByref()
    };
    public override IReadOnlyList<TypeSig> ParamSig
        => _paramSig ??= new() { Method = this };

    public override IReadOnlyList<TypeDesc> GenericParams
        => Array.Empty<TypeDesc>();

    private ParamSigList? _paramSig;

    internal MDArrayMethod(MDArrayType type, OpKind kind)
    {
        DeclaringType = type;
        Kind = kind;
    }

    public override MethodDesc GetSpec(GenericContext ctx)
    {
        var specType = DeclaringType.GetSpec(ctx);
        return specType == DeclaringType
            ? this
            : ((MDArrayType)specType).Methods[(int)Kind];
    }

    class ParamSigList : IReadOnlyList<TypeSig>
    {
        public MDArrayMethod Method = null!;

        public TypeSig this[int index] {
            get {
                if (index == 0) {
                    return Method.DeclaringType; //this
                }
                return Method.Kind switch {
                    OpKind.Set when index == Count - 1
                        => Method.DeclaringType.ElemType,
                    _ when index < Count 
                        => PrimType.Int32,
                    _
                        => throw new IndexOutOfRangeException()
                };
            }
        }

        public int Count {
            get {
                int rank = Method.DeclaringType.Rank;
                return Method.Kind switch {
                    OpKind.RangeCtor => rank * 2 + 1,
                    OpKind.Set => rank + 2,
                    _ => rank + 1
                };
            }
        }

        public IEnumerator<TypeSig> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) {
                yield return this[i];
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
