namespace DistIL;

using System.Reflection;
using System.Runtime.CompilerServices;

public class Compilation
{
    public ModuleDef Module { get; }
    public ICompilationLogger Logger { get; }
    public CompilationSettings Settings { get; }

    private TypeDef? _auxType;

    /// <summary> Returns the compiler's auxiliary type in the module. </summary>
    public TypeDef AuxType => _auxType ??= CreateAuxType();

    public Compilation(ModuleDef module, ICompilationLogger logger, CompilationSettings settings)
    {
        Module = module;
        Logger = logger;
        Settings = settings;
    }

    private TypeDef CreateAuxType()
    {
        const string kName = "<EthilAux>";
        var type = Module.FindType(null, kName);

        if (type == null) {
            type = Module.CreateType(null, kName, TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

            var attribCtor = Module.Resolver.Import(typeof(CompilerGeneratedAttribute)).FindMethod(".ctor");
            var typeAttribs = type.GetCustomAttribs(readOnly: false);
            typeAttribs.Add(new CustomAttrib(attribCtor));
        }
        return type;
    }
}

public class CompilationSettings
{
}