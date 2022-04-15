.namespace ILSamples
{
    .class public StackManipulation extends [System.Runtime]System.Object
    {
        .method public hidebysig static int32 ImplictTemps(int32& x) cil managed
        {
            .maxstack 4
            
            ldarg x
            ldind.i4
            
            ldarg x
            
            ldarg x
            ldind.i4
            ldc.i4 1
            add
            
            stind.i4
            
            ret
        }
        
        .method public static hidebysig int32 BoolAsInt_Reint(int32 x, int32 y) cil managed 
        {
            .maxstack 4
            
            ldarg x
            ldarg y
            clt
            
            ret
        }
        
        .method public static hidebysig int32 BoolAsInt_Arithm(int32 x, int32 y) cil managed 
        {
            .maxstack 4
            
            ldarg x
            ldc.i4 0
            cgt
            
            ldarg y
            add
            
            ret
        }
        
        .method public static hidebysig int32 BoolAsInt_Select(bool cond, int32 x, int32 y) cil managed 
        {
            .maxstack 4
            .locals (
                [0] int32 mask
            )
            
            //cond ? 0 : ~0
            ldarg cond
            ldc.i4 1
            sub
            stloc mask
            
            ldarg y
            ldloc mask
            and
            
            ldarg x
            ldloc mask
            not
            and
            
            or
            
            ret
        }
        
        .method public static hidebysig float64 FloatMath_MixedTypes(float64 a, float64 s) cil managed 
        {
            .maxstack 4
            
            ldarg a
            call float32 [System.Runtime]System.MathF::Sin(float32)
            
            ldarg s
            mul
        
            ret
        }
    }
}