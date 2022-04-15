namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;

using DistIL.IR;

public class MethodDef : Method, MemberDef
{
    public ModuleDef Module { get; }
    readonly MethodDefinitionHandle _handle;

    public TypeDef DeclaringType { get; }

    public MethodAttributes Attribs { get; }
    public MethodImplAttributes ImplAttribs { get; }

    public MethodBodyBlock? Body { get; }

    public MethodDef(ModuleDef mod, MethodDefinitionHandle handle)
    {
        Module = mod;
        _handle = handle;

        var reader = mod.Reader;
        var entity = reader.GetMethodDefinition(handle);

        Attribs = entity.Attributes;
        ImplAttribs = entity.ImplAttributes;
        DeclaringType = mod.GetType(entity.GetDeclaringType());
        Name = reader.GetString(entity.Name);

        IsStatic = (Attribs & MethodAttributes.Static) != 0;
        var sig = entity.DecodeSignature(mod.TypeDecoder, null);

        RetType = sig.ReturnType;
        Args = new List<Argument>(sig.RequiredParameterCount + (IsStatic ? 0 : 1));

        if (!IsStatic) {
            RType thisType = DeclaringType;
            if (thisType.IsValueType) {
                thisType = new ByrefType(thisType);
            }
            Args.Add(new Argument(thisType, 0, "this"));
        }

        var paramInfos = entity.GetParameters().GetEnumerator();

        foreach (var paramType in sig.ParameterTypes) {
            string? name = null;
            if (paramInfos.MoveNext()) {
                var info = reader.GetParameter(paramInfos.Current);
                name = reader.GetString(info.Name);
            }
            Args.Add(new Argument(paramType, Args.Count, name));
        }
        ArgTypes = Args.Select(a => a.Type).ToImmutableArray();

        if (entity.RelativeVirtualAddress != 0) {
            Body = mod.PE.GetMethodBody(entity.RelativeVirtualAddress);
        }
    }
}