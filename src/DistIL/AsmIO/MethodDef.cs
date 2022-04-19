namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using DistIL.IR;

public class MethodDef : Method, MemberDef
{
    public ModuleDef Module { get; }
    readonly MethodDefinitionHandle _handle;

    public TypeDef DeclaringType { get; }

    public MethodAttributes Attribs { get; }
    public MethodImplAttributes ImplAttribs { get; }

    private int _bodyRVA;
    private MethodBody? _body;

    public MethodBody? Body {
        get {
            if (_body == null && _bodyRVA != 0) {
                _body = new MethodBody(Module, _bodyRVA);
                _bodyRVA = 0;
            }
            return _body;
        }
        set => _body = value;
    }

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

            Ensure(info.GetMarshallingDescriptor().IsNil); //not impl
            Ensure(info.Attributes == ParameterAttributes.None);

            int index = info.SequenceNumber - 1;

            if (index >= 0 && index < Args.Count) {
                Args[index].Name = reader.GetString(info.Name);
            }
        }
        ArgTypes = Args.Select(a => a.Type).ToImmutableArray();

        _bodyRVA = entity.RelativeVirtualAddress;
    }
}

public class MethodBody
{
    public List<ExceptionRegion> ExceptionRegions { get; set; }
    public List<ILInstruction> Instructions { get; set; }
    public int MaxStack { get; set; }
    public bool InitLocals { get; set; }
    public List<Variable> Locals { get; set; }

    internal MethodBody(ModuleDef mod, int rva)
    {
        var block = mod.PE.GetMethodBody(rva);

        ExceptionRegions = DecodeExceptionRegions(mod, block);
        Instructions = DecodeInsts(mod, block.GetILReader());
        MaxStack = block.MaxStack;
        InitLocals = block.LocalVariablesInitialized;
        Locals = DecodeLocals(mod, block);
    }

    private List<ILInstruction> DecodeInsts(ModuleDef mod, BlobReader reader)
    {
        var list = new List<ILInstruction>(reader.Length / 2);
        while (reader.Offset < reader.Length) {
            var inst = DecodeInst(mod, ref reader);
            list.Add(inst);
        }
        return list;
    }

    private static ILInstruction DecodeInst(ModuleDef mod, ref BlobReader reader)
    {
        int baseOffset = reader.Offset;
        int code = reader.ReadByte();
        if (code == 0xFE) {
            code = (code << 8) | reader.ReadByte();
        }
        var opcode = (ILCode)code;
        object? operand = opcode.GetOperandType() switch {
            ILOperandType.BrTarget => reader.ReadInt32() + reader.Offset,
            ILOperandType.Field => mod.GetField(MetadataTokens.EntityHandle(reader.ReadInt32())),
            ILOperandType.Method => mod.GetMethod(MetadataTokens.EntityHandle(reader.ReadInt32())),
            ILOperandType.Sig => throw new NotImplementedException(),
            ILOperandType.String => mod.Reader.GetUserString(MetadataTokens.UserStringHandle(reader.ReadInt32())),
            ILOperandType.Tok => throw new NotImplementedException(),
            ILOperandType.Type => mod.GetType(MetadataTokens.EntityHandle(reader.ReadInt32())),
            ILOperandType.I => reader.ReadInt32(),
            ILOperandType.I8 => reader.ReadInt64(),
            ILOperandType.R => reader.ReadDouble(),
            ILOperandType.Switch => ReadJumpTable(ref reader),
            ILOperandType.Var => (int)reader.ReadUInt16(),
            ILOperandType.ShortBrTarget => (int)reader.ReadSByte() + reader.Offset,
            ILOperandType.ShortI => (int)reader.ReadSByte(),
            ILOperandType.ShortR => reader.ReadSingle(),
            ILOperandType.ShortVar => (int)reader.ReadByte(),
            _ => null
        };
        return new ILInstruction() {
            OpCode = opcode,
            Offset = baseOffset,
            Operand = operand
        };

        static int[] ReadJumpTable(ref BlobReader reader)
        {
            int count = reader.ReadInt32();
            int baseOffset = reader.Offset + count * 4;
            var targets = new int[count];

            for (int i = 0; i < count; i++) {
                targets[i] = baseOffset + reader.ReadInt32();
            }
            return targets;
        }
    }

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