.namespace ILSamples
{
    .class public Misc extends [System.Runtime]System.Object
    {        
        .method public hidebysig static int32 InvalidCodeInDeadBlock(int32 x, int32 y) cil managed
        {
            .maxstack 4
            br Cont
            ldarg x
            mul
            add
        Cont:
            ldarg x
            ldarg y
            add
            ret
        }
        .method public hidebysig static int32 InvalidCodeInDeadBlock_ConstBranch(int32 x, int32 y) cil managed
        {
            .maxstack 4
            ldc.i4 0
            ldc.i4 0
            beq Cont
            add
            mul
            ret
        Cont:
            ldarg x
            ldarg y
            add
            ret
        }
        
        .method public static hidebysig int32 Switch1(int32 x, int32 y, int32 op) cil managed 
        {
            .maxstack 4
            
            ldarg x
            ldarg y
            
            ldarg op
            switch (L_Add, L_Sub, L_Mul, L_Div)
        L_Add:
            add
            ret
        L_Sub:
            sub
            ret
        L_Mul:
            mul
            ret
        L_Div:
            div
            ret
        }
    }
}