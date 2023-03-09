namespace DistIL.Tests.AsmIO;

using DistIL.AsmIO;

[Collection("ModuleResolver")]
public class TypeTests
{
    readonly ModuleResolver _modResolver;

    public TypeTests(ModuleResolverFixture mrf)
    {
        _modResolver = mrf.Resolver;
    }

    [Fact]
    public void Test_GetCommonAncestor()
    {
        var t_String = _modResolver.Import(typeof(string));

        var t_FileStream = _modResolver.Import(typeof(FileStream));
        var t_MemoryStream = _modResolver.Import(typeof(MemoryStream));
        var t_Stream = _modResolver.Import(typeof(Stream));

        Assert.Equal(PrimType.String, TypeDesc.GetCommonAncestor(PrimType.String, t_String));

        Assert.Equal(PrimType.Object, TypeDesc.GetCommonAncestor(PrimType.String, PrimType.Object));
        Assert.Equal(PrimType.Object, TypeDesc.GetCommonAncestor(PrimType.String, PrimType.Array));

        Assert.Equal(t_Stream, TypeDesc.GetCommonAncestor(t_FileStream, t_MemoryStream));
        Assert.Equal(t_Stream, TypeDesc.GetCommonAncestor(t_FileStream, t_Stream));

        Assert.Throws<InvalidOperationException>(() => TypeDesc.GetCommonAncestor(PrimType.Int32, PrimType.Int64));
    }

    [Fact]
    public void Test_IsAssignableTo()
    {
        var t_ListInt = _modResolver.Import(typeof(List<int>));
        var t_HashSetInt = _modResolver.Import(typeof(HashSet<int>));
        var t_ICollectionInt = _modResolver.Import(typeof(ICollection<int>));
        var t_String = _modResolver.Import(typeof(string));

        var t_ListString = _modResolver.Import(typeof(List<string>));
        var t_IROListObject = _modResolver.Import(typeof(IReadOnlyList<object>));

        Assert.True(t_ListInt.IsAssignableTo(t_ICollectionInt));
        Assert.False(t_ListInt.IsAssignableTo(t_HashSetInt));

        Assert.True(PrimType.String.IsAssignableTo(PrimType.Object));
        Assert.True(t_String.IsAssignableTo(PrimType.String));
        Assert.False(t_String.IsAssignableTo(PrimType.Int32));

        Assert.True(PrimType.UInt32.IsAssignableTo(PrimType.Int32));
        Assert.True(PrimType.Int16.IsAssignableTo(PrimType.Int32));
        Assert.False(PrimType.Int32.IsAssignableTo(PrimType.Int16));

        Assert.True(PrimType.Single.IsAssignableTo(PrimType.Double));
        Assert.False(PrimType.Double.IsAssignableTo(PrimType.Single));

        Assert.True(PrimType.Byte.CreatePointer().IsAssignableTo(PrimType.Byte.CreateByref()));
        Assert.True(PrimType.Byte.CreatePointer().IsAssignableTo(PrimType.IntPtr));

        Assert.True(PrimType.Int32.CreateArray().IsAssignableTo(t_ICollectionInt));
        Assert.False(t_ICollectionInt.IsAssignableTo(PrimType.Int32.CreateArray()));

        Assert.False(t_ICollectionInt.IsAssignableTo(PrimType.Int32.CreateArray()));

        Assert.True(t_ListString.IsAssignableTo(t_IROListObject));
        Assert.False(t_ListString.IsAssignableTo(t_ICollectionInt));
        Assert.False(t_ListInt.IsAssignableTo(t_IROListObject));
    }

    [Fact]
    public void FindMethod_InGenericHierarchy()
    {
        var t_Derived3 = _modResolver.Resolve("TestAsm").FindType("TestAsm.TypeSys.Generics", "DerivedC`1");
        var derivedSpec = t_Derived3.GetSpec(PrimType.String);

        var t_TupleStrInt = _modResolver.Import(typeof(ValueTuple<string, int>));

        var t_T0 = GenericParamType.GetUnboundT(0);
        var t_MT0 = GenericParamType.GetUnboundM(0);
        var sig = new MethodSig(t_T0, new TypeSig[] { t_MT0 }, numGenPars: 1);

        var method = derivedSpec.FindMethod("GetFoo", sig, searchBaseAndItfs: true);

        Assert.Equal("DerivedA`1", method.DeclaringType.Name);
        Assert.Equal(t_TupleStrInt, method.ReturnType);
        Assert.Equal(t_MT0, method.GenericParams[0]);
    }
}