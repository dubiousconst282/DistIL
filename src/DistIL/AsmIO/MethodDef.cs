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

    public MethodBody? Body { get; }

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
        foreach (var paramType in sig.ParameterTypes) {
            string? name = null;
            Args.Add(new Argument(paramType, Args.Count, name));
        }
        foreach (var paramHandle in entity.GetParameters()) {
            var info = reader.GetParameter(paramHandle);
            int index = info.SequenceNumber - 1;
            
            if (index >= 0 && index < Args.Count) {
                Args[index].Name = reader.GetString(info.Name);
            }
        }
        ArgTypes = Args.Select(a => a.Type).ToImmutableArray();

        if (entity.RelativeVirtualAddress != 0) {
            Body = new MethodBody(mod, entity.RelativeVirtualAddress);
        }
    }
}

public class MethodBody
{
    public List<ExceptionRegion> ExceptionRegions { get; set; }
    public byte[] ILBytes { get; set; }
    public int MaxStack { get; set; }
    public bool InitLocals { get; set; }
    public List<Variable> Locals { get; set; }

    internal MethodBody(ModuleDef mod, int rva)
    {
        var block = mod.PE.GetMethodBody(rva);

        ExceptionRegions = DecodeExceptionRegions(mod, block);
        ILBytes = block.GetILBytes() ?? throw new NotImplementedException();
        MaxStack = block.MaxStack;
        InitLocals = block.LocalVariablesInitialized;
        Locals = DecodeLocals(mod, block);
    }

    public SpanReader GetILReader() => new(ILBytes);

    private static List<ExceptionRegion> DecodeExceptionRegions(ModuleDef mod, MethodBodyBlock block)
    {
        var list = new List<ExceptionRegion>(block.ExceptionRegions.Length);
        foreach (var region in block.ExceptionRegions) {
            list.Add(new() {
                Kind = region.Kind,
                CatchType = region.CatchType.IsNil ? null : mod.GetType(region.CatchType),
                HandlerOffset = region.HandlerOffset,
                HandlerLength = region.HandlerLength,
                TryOffset = region.TryOffset,
                TryLength = region.TryLength,
                FilterOffset = region.FilterOffset
            });
        }
        return list;
    }

    private static List<Variable> DecodeLocals(ModuleDef mod, MethodBodyBlock block)
    {
        if (block.LocalSignature.IsNil) {
            return new List<Variable>();
        }
        var sig = mod.Reader.GetStandaloneSignature(block.LocalSignature);
        var types = sig.DecodeLocalSignature(mod.TypeDecoder, null);
        var vars = new List<Variable>(types.Length);

        for (int i = 0; i < types.Length; i++) {
            var type = types[i];
            bool isPinned = false;
            if (type is PinnedType_ pinnedType) {
                type = pinnedType.ElemType;
                isPinned = true;
            }
            vars.Add(new Variable(type, isPinned, "loc" + (i + 1)));
        }
        return vars;
    }
}
public struct ExceptionRegion
{
    public ExceptionRegionKind Kind { get; set; }

    /// <summary> The catch type if the region represents a catch, or null otherwise. </summary>
    public RType? CatchType { get; set; }

    /// <summary> Gets the starting IL offset of the exception handler. </summary>
    public int HandlerOffset { get; set; }
    /// <summary> Gets the length in bytes of the exception handler. </summary>
    public int HandlerLength { get; set; }

    /// <summary> Gets the starting IL offset of the try block. </summary>
    public int TryOffset { get; set; }
    /// <summary> Gets the length in bytes of the try block.</summary>
    public int TryLength { get; set; }

    /// <summary> Gets the IL offset of the start of the filter block, or -1 if the region is not a filter. </summary>
    public int FilterOffset { get; set; }
}