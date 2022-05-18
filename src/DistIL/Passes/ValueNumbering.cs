namespace DistIL.Passes;

using DistIL.IR;

public class ValueNumbering : Pass
{
    public override void Transform(Method method)
    {
        foreach (var block in method) {
            TransformBlock(block);
        }
    }

    private void TransformBlock(BasicBlock block)
    {
        var tags = new Dictionary<Tag, Value>();

        foreach (var inst in block) {
            #pragma warning disable format
            (TagKind kind, Value src, Value? newVal) info = inst switch {
                LoadVarInst c       => (TagKind.Var,      c.Source, null),
                StoreVarInst c      => (TagKind.Var,      c.Dest,   c.Value),
                LoadFieldInst c     => (TagKind.Field,    c.Field,  null),
                StoreFieldInst c    => (TagKind.Field,    c.Field,  c.Value),
                LoadArrayInst c     => (TagKind.Array,    c,        null),
                StoreArrayInst c    => (TagKind.Array,    c,        c.Value),
                BinaryInst c        => (TagKind.Binary,   c,        c),
                UnaryInst c         => (TagKind.Unary,    c,        c),
                _ => default
            };
            #pragma warning restore format
            if (info.kind == TagKind.None_) continue;

            var tag = new Tag() { Kind = info.kind, Source = info.src };

            if (info.newVal == null || info.kind is TagKind.Binary or TagKind.Unary) {
                if (tags.TryGetValue(tag, out var currValue)) {
                    inst.ReplaceWith(currValue);
                } else {
                    tags[tag] = inst;
                }
            } else {
                tags[tag] = info.newVal;
            }
        }
    }

    struct Tag : IEquatable<Tag>
    {
        public TagKind Kind; //Kind of this value
        public Value Source; //Value source (backing memory, or resulting instruction)

        public bool Equals(Tag other)
        {
            return other.Kind == Kind && Kind switch {
                TagKind.Field or TagKind.Var => other.Source == Source,
                TagKind.Array => ArrEq((ArrayAccessInst)Source, (ArrayAccessInst)other.Source),
                TagKind.Binary => BinEq((BinaryInst)Source, (BinaryInst)other.Source),
                TagKind.Unary => UnEq((UnaryInst)Source, (UnaryInst)other.Source),
                _ => false
            };
            static bool ArrEq(ArrayAccessInst a, ArrayAccessInst b)
                => a.Array == b.Array && a.Index == b.Index &&
                   a.Flags == b.Flags && a.ElemType == b.ElemType;
            static bool BinEq(BinaryInst a, BinaryInst b)
                => a.Op == b.Op && a.Left.Equals(b.Left) && a.Right.Equals(b.Right);
            static bool UnEq(UnaryInst a, UnaryInst b)
                => a.Op == b.Op && a.Value.Equals(b.Value);
        }

        public override int GetHashCode()
        {
            return Kind switch {
                TagKind.Field or TagKind.Var => Source.GetHashCode(),
                TagKind.Array => ArrHash((ArrayAccessInst)Source),
                TagKind.Binary => BinHash((BinaryInst)Source),
                TagKind.Unary => UnHash((UnaryInst)Source),
                _ => 0
            };
            static int ArrHash(ArrayAccessInst inst) => HashCode.Combine(inst.Array, inst.Index);
            static int BinHash(BinaryInst inst) => HashCode.Combine(inst.Op, inst.Left, inst.Right);
            static int UnHash(UnaryInst inst) => HashCode.Combine(inst.Op, inst.Value);
        }
    }
    enum TagKind { None_, Var, Field, Array, Binary, Unary }
}