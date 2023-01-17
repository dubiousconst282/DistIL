public class CustomAttribs
{
    [TestAttrib(
        45, "CtorStr", typeof(string), new int[] { 1, 2, 3 }, 150,
        F_Type = typeof(int),
        F_Int = 550,
        F_Str = "lorem",
        F_Enum = DayOfWeek.Friday,
        F_ByteArr = new byte[] { 1, 2, 3, 255 },
        F_StrArr = new string[] { "ipsum", "dolor" },
        F_TypeArr = new Type[] { typeof(int), typeof(string), typeof(List<string>.Enumerator[]) },
        F_Boxed = 54.0,
        P_Long = 0xCAFE_1234L
    )]
    public static void DecodeCase1() { }

    public class TestAttrib : Attribute
    {
        public Type F_Type;
        public int F_Int;
        public string F_Str;
        public DayOfWeek F_Enum;
        public byte[] F_ByteArr;
        public string[] F_StrArr;
        public Type[] F_TypeArr;
        public object F_Boxed;
        public long P_Long { get; set; }

#pragma warning disable CS8618
        public TestAttrib(int a1, string a2, Type a3, int[] a4, object a5) { }
    }
}