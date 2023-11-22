namespace DistIL;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

public class Compilation
{
    public ModuleDef Module { get; }
    public ICompilationLogger Logger { get; }
    public CompilationSettings Settings { get; }

    public ModuleResolver Resolver => Module.Resolver;

    private TypeDef? _auxType;

    public Compilation(ModuleDef module, ICompilationLogger logger, CompilationSettings settings)
    {
        Module = module;
        Logger = logger;
        Settings = settings;
    }

    /// <summary> Returns the compiler's auxiliary type for the current module. </summary>
    public TypeDef GetAuxType()
    {
        if (_auxType != null) {
            return _auxType;
        }
        const string name = "<>_DistIL_Aux";
        _auxType = Module.FindType(null, name);

        if (_auxType == null) {
            _auxType = Module.CreateType(null, name, TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

            var attribCtor = Module.Resolver.Import(typeof(CompilerGeneratedAttribute)).FindMethod(".ctor");
            var typeAttribs = _auxType.GetCustomAttribs(readOnly: false);
            typeAttribs.Add(new CustomAttrib(attribCtor));
        }
        return _auxType;
    }

    /// <summary> Copies <paramref name="data"/> into a static field RVA. </summary>
    public FieldDef CreateStaticRva(ReadOnlySpan<byte> data)
    {
        Ensure.That(data.Length < 1024 * 1024 * 4, "Cannot allocate RVA bigger than 4MB");

        string fieldName = "Data_" + Convert.ToBase64String(SHA256.HashData(data)).Replace('+', '_').Replace('/', '_').TrimEnd('=');
        var auxType = GetAuxType();

        if (auxType.FindField(fieldName, throwIfNotFound: false) is FieldDef field) {
            Ensure.That(
                data.SequenceEqual(field.MappedData.AsSpan(0, data.Length)),
                "Congratulations, you just found a hash collision! (or a serious bug)");
            return field;
        }
        var (blockType, alignedSize) = CreateRvaBlock(auxType, data.Length);

        var alignedData = new byte[alignedSize];
        data.CopyTo(alignedData);

        var attrs = FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly | FieldAttributes.HasFieldRVA;
        return auxType.CreateField(fieldName, blockType, attrs, mappedData: alignedData);
    }

    private (TypeDesc Type, int Size) CreateRvaBlock(TypeDef parentType, int size)
    {
        if (size <= 8) return (PrimType.UInt64, 8);

        size = (size + 15) & ~15; // align to multiples of 16 bytes (for no particular reason).
        string name = "Block" + size;

        var blockType = parentType.FindNestedType(name);
        Ensure.That(blockType == null || blockType.LayoutSize == size);

        if (blockType == null) {
            var attrs = TypeAttributes.NestedAssembly | TypeAttributes.ExplicitLayout;
            blockType = parentType.CreateNestedType(name, attrs, baseType: Resolver.SysTypes.ValueType);
            blockType.LayoutSize = size;
        }
        return (blockType, size);
    }
}

public class CompilationSettings
{
    /// <summary> If set to true, the resulting module will have a fixed dependency on a little-endianess. </summary>
    public bool AssumeLittleEndian { get; init; } = true; // TODO: add module cctor check
}