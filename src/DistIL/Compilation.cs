namespace DistIL;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

using DistIL.Analysis;

public class Compilation
{
    public ModuleDef Module { get; }
    public ICompilationLogger Logger { get; }
    public CompilationSettings Settings { get; }

    public ModuleResolver Resolver => Module.Resolver;

    private TypeDef? _auxType;

    readonly Dictionary<Type, IGlobalAnalysis> _analyses = new();

    public Compilation(ModuleDef module, ICompilationLogger logger, CompilationSettings settings)
    {
        Module = module;
        Logger = logger;
        Settings = settings;

        EnsureMembersAccessible(module.Resolver.CoreLib);
        EnsureMembersAccessible(module);
    }

    public A GetAnalysis<A>() where A : IGlobalAnalysis
    {
        ref var analysis = ref _analyses.GetOrAddRef(typeof(A));
        analysis ??= A.Create(this);
        return (A)analysis;
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

    HashSet<string> _assembliesToIgnoreAccessChecksFor = new();

    /// <summary> Adds an IgnoresAccessChecksToAttribute for the given module if there's not already one. </summary>
    /// <remarks> This ensures that <see cref="Module"/> can access any private member declared in the given module.  </remarks>
    public void EnsureMembersAccessible(ModuleDef module)
    {
        string name = module.AsmName.Name!;

        if (_assembliesToIgnoreAccessChecksFor.Add(name)) {
            MarkIgnoreAccessChecksToAssembly(name);
        }
    }

    private MethodDesc? m_IgnoreAccessChecksToAttributeCtor;

    private void MarkIgnoreAccessChecksToAssembly(string assemblyName)
    {
        var asmAttribs = Module.GetCustomAttribs(forAssembly: true);

        if (m_IgnoreAccessChecksToAttributeCtor == null) {
            var attribType = Module.FindType("System.Runtime.CompilerServices", "IgnoresAccessChecksToAttribute");

            if (attribType == null) {
                attribType = Module.CreateType(
                    "System.Runtime.CompilerServices", "IgnoresAccessChecksToAttribute",
                    TypeAttributes.BeforeFieldInit,
                    Module.Resolver.CoreLib.FindType("System", "Attribute")
                );
                var attribCtor = attribType.CreateMethod(
                    ".ctor", PrimType.Void,
                    [new ParamDef(attribType, "this"), new ParamDef(PrimType.String, "assemblyName")],
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName
                );
                attribCtor.ILBody = new ILMethodBody() {
                    Instructions = new[] { new ILInstruction(ILCode.Ret) }
                };

                m_IgnoreAccessChecksToAttributeCtor = attribCtor;
            } else {
                m_IgnoreAccessChecksToAttributeCtor = attribType.FindMethod(".ctor", new MethodSig(PrimType.Void, [PrimType.String]));

                foreach (var attrib in asmAttribs) {
                    if (attrib.Constructor == m_IgnoreAccessChecksToAttributeCtor) {
                        _assembliesToIgnoreAccessChecksFor.Add((string)attrib.Args[0]);
                    }
                }
            }
        }
        asmAttribs.Add(new CustomAttrib(m_IgnoreAccessChecksToAttributeCtor, [assemblyName]));
    }
}

public class CompilationSettings
{
    /// <summary> If set to true, the resulting module will have a fixed dependency on a little-endianess. </summary>
    public bool AssumeLittleEndian { get; init; } = true; // TODO: add module cctor check

    /// <summary> Whether to allow optimizations to inline or make assumptions about methods defined in other assemblies. </summary>
    public bool AllowCrossAssemblyIPO { get; init; } = true;
}