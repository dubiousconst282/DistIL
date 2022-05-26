using DistIL.AsmIO;
using DistIL.IR;

public class ConstTests
{
    [Fact]
    public void Test_CreateZero()
    {
        void Check(Const c) => Assert.Equal(0, ((ConstInt)c).Value);

        Check(ConstInt.CreateZero(PrimType.Byte));
        Check(ConstInt.CreateZero(PrimType.UInt16));
        Check(ConstInt.CreateZero(PrimType.UInt32));
        Check(ConstInt.CreateZero(PrimType.UInt64));
    }

    [Fact]
    public void Test_ConstInt_Normalization()
    {
        void Check(PrimType type, long min, long max, ulong minU)
        {
            Assert.Equal(min, ConstInt.Create(type, min).Value);
            Assert.Equal(max, ConstInt.Create(type, max).Value);

            Assert.Equal(minU, ConstInt.Create(type, min).UValue);
            Assert.Equal((ulong)max, ConstInt.Create(type, max).UValue);

            var ci = ConstInt.Create(type, max + 1);
            Assert.Equal(ci.IsSigned ? min : 0, ci.Value);
            Assert.Equal(minU, ci.UValue);

            ci = ConstInt.Create(type, min - 1);
            Assert.Equal(max, ci.Value);
            Assert.Equal((ulong)max, ci.UValue);
        }
        unchecked { //I swear this is the most annoying and useless feature of C#
            Check(PrimType.Byte, Byte.MinValue, Byte.MaxValue, Byte.MinValue);
            Check(PrimType.SByte, SByte.MinValue, SByte.MaxValue, (byte)SByte.MinValue);
            Check(PrimType.Int16, Int16.MinValue, Int16.MaxValue, (ushort)Int16.MinValue);
            Check(PrimType.UInt16, UInt16.MinValue, UInt16.MaxValue, UInt16.MinValue);
            Check(PrimType.Int32, Int32.MinValue, Int32.MaxValue, (uint)Int32.MinValue);
            Check(PrimType.UInt32, UInt32.MinValue, UInt32.MaxValue, UInt32.MinValue);
            Check(PrimType.Int64, Int64.MinValue, Int64.MaxValue, (ulong)Int64.MinValue);
            Check(PrimType.UInt64, (long)UInt64.MinValue, (long)UInt64.MaxValue, UInt64.MinValue);
        }
    }
}