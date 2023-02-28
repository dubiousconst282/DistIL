namespace DistIL.Passes;

using System.Numerics;

using DistIL.IR.Utils;
using DistIL.Passes.Vectorization;

public class SlpVectorizer : IMethodPass
{
    float _costThreshold = -1; //setting to +Inf will bypass legality checks
    VectorFuncTable _funcTable;

    public SlpVectorizer(ModuleResolver resolver)
    {
        _funcTable = new VectorFuncTable(resolver);
    }

    static IMethodPass IMethodPass.Create<TSelf>(Compilation comp)
        => new SlpVectorizer(comp.Resolver);

    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var storeSeeds = new MultiDictionary<Value, StoreSeed>();
        var stamper = new VectorTreeStamper();
        bool changed = false;

        foreach (var block in ctx.Method) {
            stamper.Reset();
            //Find seeds
            foreach (var inst in block) {
                if (inst is StorePtrInst store && VectorType.IsSupportedElemType(store.ElemType)) {
                    var addr = AddrInfo.Decompose(store.Address);
                    storeSeeds.Add(addr.BasePtr, new() { Store = store, Addr = addr });
                }
                stamper.AddInst(inst);
            }
            //Consider seeds
            foreach (var (addr, bucket) in storeSeeds) {
                if (bucket.Count < 2 || !BitOperations.IsPow2(bucket.Count)) continue;

                var stores = bucket.AsSpan();

                //Sort bucket so that consecutive stores are next to each other
                stores.Sort((a, b) => a.Addr.SameIndex(b.Addr) ? a.Addr.Displacement - b.Addr.Displacement : +1);

                //Break up stores into vector-size chunks and try vectorize them
                for (int i = 0; i < stores.Length; ) {
                    var vecType = GetTypeForConsecutiveStores(stores.Slice(i));
                    var chunk = stores.Slice(i, vecType.Count);

                    if (!vecType.IsEmpty && TryVectorizeStores(chunk, vecType, stamper)) {
                        i += vecType.Count;
                        changed = true;
                    } else {
                        i++;
                    }
                }
            }
            storeSeeds.Clear();
        }

        return changed ? MethodInvalidations.DataFlow : 0;
    }

    private static VectorType GetTypeForConsecutiveStores(Span<StoreSeed> stores)
    {
        var elemType = stores[0].Store.ElemType;
        int elemWidth = elemType.Kind.BitSize();
        int maxWidth = Math.Min(VectorType.MaxBitWidth / elemWidth, stores.Length);
        int i = 1;

        for (; i < maxWidth; i++) {
            if (stores[i].Store.ElemType != elemType) break;
            if (!stores[i].Addr.IsNextAdjacent(stores[i - 1].Addr)) break;
        }
        return VectorType.GetBiggest(elemType, i);
    }

    private bool TryVectorizeStores(Span<StoreSeed> seeds, VectorType vecType, VectorTreeStamper stamper)
    {
        var lanes = new Value[seeds.Length];
        for (int i = 0; i < lanes.Length; i++) {
            lanes[i] = seeds[i].Store.Value;
        }
        var (tree, cost) = stamper.BuildTree(vecType, lanes);

        if (cost < _costThreshold) {
            //FIXME: should build after the last store in the block, not in the group
            var builder = new IRBuilder(seeds[^1].Store, InsertionDir.After);
            var value = tree.Emit(builder, _funcTable);

            _funcTable.BuildCall(builder, vecType, "StoreUnsafe:", value, seeds[0].Store.Address);

            foreach (var seed in seeds) {
                seed.Store.Remove();
            }
            //FIXME: we probably need to rebuild indices after this
            stamper.ResetTree(removeFibers: true);
            return true;
        }
        return false;
    }

    struct StoreSeed
    {
        public StorePtrInst Store;
        public AddrInfo Addr;
    }
}