namespace DistIL.Tests.AsmIO;

using DistIL.AsmIO;

[Collection("ModuleResolver")]
public class CustomAttribTests
{
    readonly ModuleResolver _modResolver;

    public CustomAttribTests(ModuleResolverFixture mrf)
    {
        _modResolver = mrf.Resolver;
    }

    [Fact]
    public void Test_BlobDecoding()
    {
        var sys = _modResolver.SysTypes;

        var type = _modResolver.Load("DistIL.Tests.TestAsm.dll").FindType(null, "CustomAttribs")!;
        var attrib = type.Methods.First(m => m.Name == "DecodeCase1").GetCustomAttribs().First();

        Assert.Equal(type, ((TypeDef)attrib.Constructor.DeclaringType).DeclaringType);
        Assert.Equal(5, attrib.FixedArgs.Length);
        Assert.Equal(9, attrib.NamedArgs.Length);

        Assert.Equal(45, attrib.FixedArgs[0]);
        Assert.Equal("CtorStr", attrib.FixedArgs[1]);
        Assert.Equal(sys.String, attrib.FixedArgs[2]);
        Assert.Equal(new int[] { 1, 2, 3 }, (int[]?)attrib.FixedArgs[3]);
        Assert.Equal(150, attrib.FixedArgs[4]);

        var listEnumeratorArray = _modResolver.Import(typeof(List<string>.Enumerator)).CreateArray();

        CheckNamed("F_Type", sys.Type, sys.Int32);
        CheckNamed("F_Int", PrimType.Int32, 550);
        CheckNamed("F_Str", PrimType.String, "lorem");
        CheckNamed("F_Enum", _modResolver.Import(typeof(DayOfWeek))!, (int)DayOfWeek.Friday);
        CheckNamed("F_ByteArr", PrimType.Byte.CreateArray(), new byte[] { 1, 2, 3, 255 });
        CheckNamed("F_StrArr", PrimType.String.CreateArray(), new string[] { "ipsum", "dolor" });
        CheckNamed("F_TypeArr", sys.Type.CreateArray(), new TypeDesc[] { sys.Int32, sys.String, listEnumeratorArray });
        CheckNamed("F_Boxed", PrimType.Object, 54.0);
        CheckNamed("P_Long", PrimType.Int64, 0xCAFE_1234L);

        void CheckNamed(string name, TypeDesc type, object? value)
        {
            var prop = attrib.GetNamedArg(name)!;
            Assert.Equal(name, prop.Name);
            Assert.Equal(type, prop.Type);
            Assert.Equal(value, prop.Value);
        }
    }
}