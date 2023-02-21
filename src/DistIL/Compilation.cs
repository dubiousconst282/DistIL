namespace DistIL;

using System.Reflection;
using System.Runtime.CompilerServices;

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

    /// <summary> Returns the compiler's auxiliary type for the module. </summary>
    public TypeDef GetAuxType()
    {
        if (_auxType != null) {
            return _auxType;
        }
        const string name = "<EthilAux>";
        _auxType = Module.FindType(null, name);

        if (_auxType == null) {
            _auxType = Module.CreateType(null, name, TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

            var attribCtor = Module.Resolver.Import(typeof(CompilerGeneratedAttribute)).FindMethod(".ctor");
            var typeAttribs = _auxType.GetCustomAttribs(readOnly: false);
            typeAttribs.Add(new CustomAttrib(attribCtor));
        }
        return _auxType;
    }
}

public class CompilationSettings
{
}