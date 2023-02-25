namespace DistIL.AsmIO;

using PropArray = ImmutableArray<CustomAttribProp>;
using ValueArray = ImmutableArray<object?>;

public partial class CustomAttrib
{
    ValueArray _fixedArgs;
    PropArray _namedArgs;
    
    byte[]? _encodedBlob; //as specified in `II.23.3 Custom attributes`
    ModuleDef? _parentModule;
    bool _decoded = false;

    public MethodDesc Constructor { get; }
    public ValueArray FixedArgs {
        get {
            EnsureDecoded();
            return _fixedArgs;
        }
    }
    public PropArray NamedArgs {
        get {
            EnsureDecoded();
            return _namedArgs;
        }
    }
    public TypeDesc Type => Constructor.DeclaringType;

    public CustomAttrib(MethodDesc ctor, byte[] encodedBlob, ModuleDef parentModule)
    {
        Constructor = ctor;
        _encodedBlob = encodedBlob;
        _parentModule = parentModule;
    }
    public CustomAttrib(MethodDesc ctor, ValueArray fixedArgs = default, PropArray namedArgs = default)
    {
        Constructor = ctor;
        _fixedArgs = fixedArgs.EmptyIfDefault();
        _namedArgs = namedArgs.EmptyIfDefault();
        _decoded = true;

        Ensure.That(_fixedArgs.Length == (ctor.ParamSig.Count - 1));
    }
    
    public CustomAttribProp? GetNamedArg(string name)
    {
        return NamedArgs.FirstOrDefault(a => a.Name == name);
    }

    private void EnsureDecoded()
    {
        if (!_decoded) {
            DecodeBlob();
            _decoded = true;
        }
    }

    public byte[] GetEncodedBlob()
    {
        return _encodedBlob ??= EncodeBlob();
    }
}
public class CustomAttribProp
{
    public required TypeDesc Type { get; init; }
    public required string Name { get; init; }
    public required bool IsField { get; init; }
    public required object? Value { get; init; }

    public override string ToString() => $"{Name}: {Type} = '{Value}'";
}

public static class CustomAttribExt
{
    internal static IList<CustomAttrib> GetOrInitList(ref IList<CustomAttrib>? list, bool readOnly)
    {
        var attribs = list ?? Array.Empty<CustomAttrib>();

        if (!readOnly && list is not List<CustomAttrib>) {
            return list = new List<CustomAttrib>(attribs);
        }
        return attribs;
    }

    public static CustomAttrib? Find(this IList<CustomAttrib> list, string? ns, string className)
    {
        return list.FirstOrDefault(ca => {
            var declType = ca.Constructor.DeclaringType;
            return declType.Name == className && declType.Namespace == ns;
        });
    }

    public static CustomAttrib? Find(this IList<CustomAttrib> list, Type attribType)
        => list.Find(attribType.Namespace, attribType.Name);

    public static bool HasCustomAttrib(this ModuleEntity entity, Type attribType)
        => entity.GetCustomAttribs().Find(attribType) != null;

    public static bool HasCustomAttrib(this ModuleEntity entity, string? ns, string className)
        => entity.GetCustomAttribs().Find(ns, className) != null;
}