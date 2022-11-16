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