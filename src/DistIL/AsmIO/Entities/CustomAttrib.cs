namespace DistIL.AsmIO;

using System.Reflection.Metadata;

using PropArray = ImmutableArray<CustomAttribProp>;
using ValueArray = ImmutableArray<object?>;

public partial class CustomAttrib
{
    ValueArray _fixedArgs;
    PropArray _namedArgs;
    
    byte[]? _encodedBlob; // as specified in `II.23.3 Custom attributes`
    ModuleDef? _parentModule;
    CustomAttributeHandle _handle;

    MethodDesc? _ctor;
    public MethodDesc Constructor {
        get {
            EnsureLoaded();
            return _ctor!;
        }
    }
    public ValueArray Args {
        get {
            EnsureDecoded();
            return _fixedArgs;
        }
    }
    public PropArray Properties {
        get {
            EnsureDecoded();
            return _namedArgs;
        }
    }
    public TypeDesc Type => Constructor.DeclaringType;

    // Note that this will be called while the method table is still being parsed, so the ctor may be unavailable.
    internal CustomAttrib(ModuleDef parentModule, CustomAttributeHandle handle)
    {
        _parentModule = parentModule;
        _handle = handle;
    }
    public CustomAttrib(MethodDesc ctor, ValueArray fixedArgs = default, PropArray namedArgs = default)
    {
        _ctor = ctor;
        _fixedArgs = fixedArgs.EmptyIfDefault();
        _namedArgs = namedArgs.EmptyIfDefault();

        Ensure.That(_fixedArgs.Length == (ctor.ParamSig.Count - 1));
    }
    
    public CustomAttribProp? GetProperty(string name)
    {
        return Properties.FirstOrDefault(a => a.Name == name);
    }

    public byte[] GetEncodedBlob()
    {
        EnsureLoaded();
        return _encodedBlob ??= EncodeBlob();
    }

    private void EnsureDecoded()
    {
        EnsureLoaded();

        if (_fixedArgs.IsDefault) {
            DecodeBlob();
            Debug.Assert(!_fixedArgs.IsDefault);
        }
    }
    private void EnsureLoaded()
    {
        if (_handle.IsNil || _ctor != null) return;

        var reader = _parentModule!._loader!._reader;
        var attr = reader.GetCustomAttribute(_handle);

        _ctor = (MethodDesc)_parentModule._loader.GetEntity(attr.Constructor);
        _encodedBlob = reader.GetBlobBytes(attr.Value);
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

public static class CustomAttribUtils
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