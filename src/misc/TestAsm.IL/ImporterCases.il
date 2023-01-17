.class public ImporterCases
{
    .method public static void PinnedVar1(int32& x)
    {
        .locals ( int32& pinned temp )
        
        ldarg x
        stloc temp

        ldloc temp
        call void ImporterCases::MutPtr(int32*)

        ldc.i4 0
        stloc temp

        ret
    }

    .method private static void MutPtr(int32* ptr)
    {
        //*ptr ^= 0x5555
        ldarg ptr
        dup
        ldind.i4

        ldc.i4 0x5555
        xor
        stind.i4
        
        ret
    }
}