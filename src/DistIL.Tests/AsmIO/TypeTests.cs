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
    public void Test_GetCommonType_Primitives()
    {
        Assert.Null(TypeDesc.GetCommonAssignableType(null, null));
        Assert.Equal(PrimType.Int32, TypeDesc.GetCommonAssignableType(null, PrimType.Int32));
        Assert.Equal(PrimType.Int32, TypeDesc.GetCommonAssignableType(PrimType.Int32, null));

        Assert.Equal(PrimType.Int32, TypeDesc.GetCommonAssignableType(PrimType.Bool, PrimType.Int32));
        Assert.Equal(PrimType.UInt16, TypeDesc.GetCommonAssignableType(PrimType.UInt16, PrimType.Char));

        Assert.Equal(PrimType.Int32, TypeDesc.GetCommonAssignableType(PrimType.Byte, PrimType.Int32));
        Assert.Equal(PrimType.Int32, TypeDesc.GetCommonAssignableType(PrimType.Int32, PrimType.Int16));

        Assert.Equal(PrimType.Int32, TypeDesc.GetCommonAssignableType(PrimType.UInt32, PrimType.Int32));
        Assert.Equal(PrimType.UInt32, TypeDesc.GetCommonAssignableType(PrimType.UInt32, PrimType.UInt32));

        Assert.Equal(PrimType.Double, TypeDesc.GetCommonAssignableType(PrimType.Single, PrimType.Double));

        Assert.Equal(PrimType.IntPtr, TypeDesc.GetCommonAssignableType(PrimType.Int32, PrimType.IntPtr));
        Assert.Equal(PrimType.Void.CreateByref(), TypeDesc.GetCommonAssignableType(PrimType.IntPtr, PrimType.Byte.CreateByref()));
        Assert.Equal(PrimType.Void.CreatePointer(), TypeDesc.GetCommonAssignableType(PrimType.IntPtr, PrimType.Byte.CreatePointer()));

        Assert.Null(TypeDesc.GetCommonAssignableType(PrimType.Int32, PrimType.Int64));
        Assert.Null(TypeDesc.GetCommonAssignableType(PrimType.Int32, PrimType.Single));
        Assert.Null(TypeDesc.GetCommonAssignableType(PrimType.Object, PrimType.Int32));

        Assert.Equal(PrimType.Object, TypeDesc.GetCommonAssignableType(PrimType.String, PrimType.Object));
    }

    [Fact]
    public void Test_GetCommonAncestor()
    {
        var listType = _modResolver.Import(typeof(List<int>));
        var setType = _modResolver.Import(typeof(HashSet<int>));
        var collectionType = _modResolver.Import(typeof(ICollection<int>));
        var stringType = _modResolver.Import(typeof(string));

        Assert.Equal(collectionType, TypeDesc.GetCommonAncestor(listType, setType));
        Assert.Equal(PrimType.String, TypeDesc.GetCommonAncestor(PrimType.String, stringType));

        Assert.Equal(PrimType.Object, TypeDesc.GetCommonAncestor(PrimType.String, PrimType.Object));
        Assert.Equal(PrimType.Object, TypeDesc.GetCommonAncestor(PrimType.String, PrimType.Array));

        Assert.Equal(PrimType.Object, TypeDesc.GetCommonAncestor(PrimType.String, PrimType.Array));

        Assert.Throws<InvalidOperationException>(() => TypeDesc.GetCommonAncestor(PrimType.Int32, PrimType.Int64));
    }

    [Fact]
    public void Test_IsAssignableTo()
    {
        var listType = _modResolver.Import(typeof(List<int>));
        var setType = _modResolver.Import(typeof(HashSet<int>));
        var collectionType = _modResolver.Import(typeof(ICollection<int>));
        var stringType = _modResolver.Import(typeof(string));

        Assert.True(listType.IsAssignableTo(collectionType));
        Assert.False(listType.IsAssignableTo(setType));

        Assert.True(PrimType.String.IsAssignableTo(PrimType.Object));
        Assert.True(stringType.IsAssignableTo(PrimType.String));
        Assert.False(stringType.IsAssignableTo(PrimType.Int32));

        Assert.True(PrimType.UInt32.IsAssignableTo(PrimType.Int32));
        Assert.True(PrimType.Int16.IsAssignableTo(PrimType.Int32));
        Assert.False(PrimType.Int32.IsAssignableTo(PrimType.Int16));

        Assert.True(PrimType.Single.IsAssignableTo(PrimType.Double));
        Assert.False(PrimType.Double.IsAssignableTo(PrimType.Single));

        Assert.True(PrimType.Byte.CreatePointer().IsAssignableTo(PrimType.Byte.CreateByref()));
        Assert.True(PrimType.Byte.CreatePointer().IsAssignableTo(PrimType.IntPtr));
    }
}