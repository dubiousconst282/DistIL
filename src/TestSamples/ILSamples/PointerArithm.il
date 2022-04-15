.namespace ILSamples
{
    .class public PointerArithm extends [System.Runtime]System.Object
    {
        .method public static hidebysig int64 WeirdAliasing1(uint8& src) cil managed 
        {
            .maxstack 4
            
            ldarg src
            ldind.i1
            
            ldarg src
            ldind.i2
            
            ldarg src
            ldind.i4
            add
            add
            conv.i8
            
            ldarg src
            ldind.i8
            add
            
            ldarg src
            call instance int64 [System.Runtime]System.DateTime::get_Ticks()
            add
            
            ret
        }
        
        .method public static hidebysig void MemCopy(uint8& src, uint8& dst, int32 count) cil managed 
        {
            .maxstack 4
            .locals (
                [0] uint8& srcEnd
            )
            //srcEnd = src + count;
            ldarg src
            ldarg count
            add
            stloc srcEnd
        
        CopyBlock8:
            //if (src + 8 > srcEnd) goto EndBlock8;
            ldarg src
            ldc.i4 8
            add
            ldloc srcEnd
            bgt EndBlock8
            
            //*(uint64*)dst = *(uint64*)src;
            ldarg dst
            
            ldarg src
            unaligned. 1
            ldind.i8
            
            unaligned. 1
            stind.i8
            
            //src += 8;
            ldarg src
            ldc.i4 8
            add
            starg src
            //dst += 8;
            ldarg dst
            ldc.i4 8
            add
            starg dst
            
            br CopyBlock8
        EndBlock8:
        
        CopyBlock1:
            //if (src > srcEnd) goto EndBlock1;
            ldarg src
            ldloc srcEnd
            bgt EndBlock1
            
            //*(uint8*)dst = *(uint8*)src;
            ldarg dst
            
            ldarg src
            ldind.i1
            
            stind.i1
            
            //src += 1;
            ldarg src
            ldc.i4 1
            add
            starg src
            //dst += 1;
            ldarg dst
            ldc.i4 1
            add
            starg dst
            
            br CopyBlock1
        EndBlock1:
        
            ret
        }
    }
}