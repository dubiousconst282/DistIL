using System.Reflection.Metadata;

using DistIL.AsmIO;
using DistIL.CodeGen.Cil;

public class ILAssemblerTests
{
    [Fact]
    public void TestSimple1()
    {
        var asm = new ILAssembler();
        //return arg0 + arg1 * 2.0
        asm.Emit(ILCode.Ldarg_0);
        asm.Emit(ILCode.Ldarg_1);
        asm.Emit(ILCode.Ldc_R8, 2.0);
        asm.Emit(ILCode.Mul);
        asm.Emit(ILCode.Add);
        asm.Emit(ILCode.Ret);

        var rawInsts = asm.Bake().ToArray();
        var genInsts = rawInsts.Select(v => (v.Offset, v.OpCode, v.Operand));

        var expInsts = new (int, ILCode, object)[] {
            (0,  ILCode.Ldarg_0, null),
            (1,  ILCode.Ldarg_1, null),
            (2,  ILCode.Ldc_R8,  2.0),
            (11, ILCode.Mul,     null),
            (12, ILCode.Add,     null),
            (13, ILCode.Ret,     null),
        };

        Assert.Equal(expInsts, genInsts);
    }

    [Fact]
    public void TestLabels()
    {
        var asm = new ILAssembler();
        var lblGt  = asm.DefineLabel();
        var lblRet = asm.DefineLabel();

        //return arg1 > arg0 ? arg1 : arg0
        asm.Emit(ILCode.Ldarg_1);
        asm.Emit(ILCode.Ldarg_0);
        asm.Emit(ILCode.Bgt, lblGt);

        asm.Emit(ILCode.Ldarg_0);
        asm.Emit(ILCode.Br, lblRet);

        asm.MarkLabel(lblGt);
        asm.Emit(ILCode.Ldarg_1);

        asm.MarkLabel(lblRet);
        asm.Emit(ILCode.Ret);

        var rawInsts = asm.Bake().ToArray();
        var genInsts = rawInsts.Select(v => (v.Offset, v.OpCode, v.Operand));

        var expInsts = new (int, ILCode, object)[] {
            (0, ILCode.Ldarg_1, null),
            (1, ILCode.Ldarg_0, null),
            (2, ILCode.Bgt_S,   7),
            (4, ILCode.Ldarg_0, null),
            (5, ILCode.Br_S,    8),
            (7, ILCode.Ldarg_1, null),
            (8, ILCode.Ret,     null),
        };

        Assert.Equal(expInsts, genInsts);
    }
}