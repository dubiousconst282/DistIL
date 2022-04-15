namespace DistIL.Frontend;

[Flags]
internal enum InstFlags
{
    None            = 0,

    Unaligned       = 1 << 0,
    Volatile        = 1 << 1,
    Tailcall        = 1 << 2,
    Constrained     = 1 << 3,
    Readonly        = 1 << 4,

    //Bits [16..23] are reserved for `no.` prefix
    NoPrefixShift_  = 16,
    NoTypeCheck     = 1 << 16,
    NoRangeCheck    = 1 << 17,
    NoNullCheck     = 1 << 18,
}