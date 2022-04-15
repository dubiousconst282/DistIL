namespace DistIL.IR;

public class Intrinsic : Callsite
{
    public IntrinsicId Id { get; }

    public Intrinsic(IntrinsicId id, RType retType, params RType[] argTypes)
    {
        Ensure(id != IntrinsicId.None);
        Name = "$" + id.ToString();
        RetType = retType;
        ArgTypes = argTypes.ToImmutableArray();
        IsStatic = true;
    }
}

public enum IntrinsicId
{
    None,           //not a real intrinsic

    NewArray,       //T[] newarr<T[]>(int|nint length)

    CheckFinite,    //float ckfinite(float), throw if x is NaN or +-Infinity
    MemCopy,        //void cpblk(void*|void& dst, void*|void& src, uint len)
    MemSet,         //void initblk(void*|void& dst, byte val, uint len)

    CopyObj,        //void cpobj<T>(T*|T& dst, T*|T& src)
    InitObj,        //void initobj<T>(T*|T& dst)

    SizeOf,         //uint sizeof<T>()
    
    CastClass,      //R castclass<T, R>(T obj)
    IsInstance,     //bool isinst<T>(T obj)

    LoadToken,      

    Box,
    Unbox
}