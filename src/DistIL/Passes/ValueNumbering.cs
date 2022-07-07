namespace DistIL.Passes;

using DistIL.IR;

public class ValueNumbering : MethodPass
{
    public override void Run(MethodTransformContext ctx)
    {
        foreach (var block in ctx.Method) {
            TransformBlock(block);
        }
    }

    private void TransformBlock(BasicBlock block)
    {
        var currDefs = new Dictionary<Tag, Value>();

        foreach (var inst in block) {
            #pragma warning disable format
            (TagKind kind, Value src, Value? newVal) info = inst switch {
                LoadVarInst c       => (TagKind.Var,      c.Var,    null),
                StoreVarInst c      => (TagKind.Var,      c.Var,    c.Value),
                LoadFieldInst c     => (TagKind.Field,    c,        null),
                StoreFieldInst c    => (TagKind.Field,    c,        c.Value),
                LoadArrayInst c     => (TagKind.Array,    c,        null),
                StoreArrayInst c    => (TagKind.Array,    c,        c.Value),
                BinaryInst c        => (TagKind.Binary,   c,        c),
                UnaryInst c         => (TagKind.Unary,    c,        c),
                _ => default
            };
            #pragma warning restore format
            if (info.kind == TagKind.None_) continue;

            var tag = new Tag() { Kind = info.kind, Source = info.src };

            if (info.newVal == null || info.newVal == info.src) {
                if (currDefs.TryGetValue(tag, out var currValue)) {
                    inst.ReplaceWith(currValue);
                } else {
                    currDefs[tag] = inst;
                }
            } else {
                currDefs[tag] = info.newVal;
            }
        }
    }

    struct Tag : IEquatable<Tag>
    {
        public TagKind Kind; //Kind of this value
        public Value Source; //Value source (var/ptr) or result instruction

        public bool Equals(Tag other)
        {
            if (other.Kind != Kind) {
                return false;
            }
            switch (Kind) {
                case TagKind.Var: {
                    return Source == other.Source;
                }
                case TagKind.Field: {
                    var (a, b) = ((FieldAccessInst)Source, (FieldAccessInst)other.Source);
                    return a.Field == b.Field && a.Obj == b.Obj;
                }
                case TagKind.Array: {
                    var (a, b) = ((ArrayAccessInst)Source, (ArrayAccessInst)other.Source);
                    return a.Array == b.Array && a.Index == b.Index &&
                           a.Flags == b.Flags && a.ElemType == b.ElemType;
                }
                case TagKind.Binary: {
                    var (a, b) = ((BinaryInst)Source, (BinaryInst)other.Source);
                    return a.Op == b.Op && a.Left.Equals(b.Left) && a.Right.Equals(b.Right);
                }
                case TagKind.Unary: {
                    var (a, b) = ((UnaryInst)Source, (UnaryInst)other.Source);
                    return a.Op == b.Op && a.Value.Equals(b.Value);
                }
                default: return false;
            }
        }

        public override int GetHashCode()
        {
            switch (Kind) {
                case TagKind.Var: {
                    return HashCode.Combine(Source);
                }
                case TagKind.Field: {
                    var a = (FieldAccessInst)Source;
                    return HashCode.Combine(a.Field, a.Obj);
                }
                case TagKind.Array: {
                    var a = (ArrayAccessInst)Source;
                    return HashCode.Combine(a.Array, a.Index);
                }
                case TagKind.Binary: {
                    var a = (BinaryInst)Source;
                    return HashCode.Combine(a.Op, a.Left, a.Right);
                }
                case TagKind.Unary: {
                    var a = (UnaryInst)Source;
                    return HashCode.Combine(a.Op, a.Value);
                }
                default: return 0;
            }
        }
    }
    enum TagKind { None_, Var, Field, Array, Binary, Unary }
}