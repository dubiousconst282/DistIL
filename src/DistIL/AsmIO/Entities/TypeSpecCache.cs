namespace DistIL.AsmIO;

using System.Runtime.CompilerServices;

internal class TypeSpecCache
{
    // TODO: experiment with WeakRefs/ConditionalWeakTable
    readonly Dictionary<SpecKey, TypeSpec> _entries = new();

    public ref TypeSpec? Get(IReadOnlyList<TypeDesc> pars, GenericContext ctx, out ImmutableArray<TypeDesc> filledArgs)
    {
        var key = new SpecKey(pars, ctx);
        ref var slot = ref _entries.GetOrAddRef(key, out bool exists);
        filledArgs = exists ? default : key.GetArgs();
        return ref slot;
    }

    readonly struct SpecKey : IEquatable<SpecKey>
    {
        readonly object _data; // Either<TypeDesc, TypeDesc[]>

        public SpecKey(IReadOnlyList<TypeDesc> pars, GenericContext ctx)
        {
            if (pars.Count == 1) {
                _data = pars[0].GetSpec(ctx);
            } else {
                var args = ctx.FillParams(pars);
                // take the internal array directly to avoid boxing
                _data = Unsafe.As<ImmutableArray<TypeDesc>, TypeDesc[]>(ref args);
            }
        }

        public ImmutableArray<TypeDesc> GetArgs()
        {
            return _data is TypeDesc[] arr
                ? Unsafe.As<TypeDesc[], ImmutableArray<TypeDesc>>(ref arr)
                : ImmutableArray.Create((TypeDesc)_data);
        }

        public bool Equals(SpecKey other)
        {
            if (_data is TypeDesc[] sig) {
                return other._data is TypeDesc[] otherSig && sig.AsSpan().SequenceEqual(otherSig);
            }
            return _data.Equals(other._data); // TypeDesc
        }

        public override int GetHashCode()
        {
            if (_data is TypeDesc[] sig) {
                var hash = new HashCode();
                foreach (var type in sig) {
                    hash.Add(type);
                }
                return hash.ToHashCode();
            }
            return _data.GetHashCode();
        }

        public override bool Equals(object? obj)
            => throw new InvalidOperationException();
    }
}