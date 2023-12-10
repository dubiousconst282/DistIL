namespace DistIL.AsmIO;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

public class ILMethodBody
{
    public ArraySegment<ILInstruction> Instructions { get; set; }
    public ILVariable[] Locals { get; set; } = [];
    public ExceptionClause[] ExceptionClauses { get; set; } = [];
    public int MaxStack { get; set; }
    public bool InitLocals { get; set; }

    internal ILMethodBody(ModuleLoader loader, int rva)
    {
        var block = loader._pe.GetMethodBody(rva);
        Instructions = DecodeInsts(loader, block.GetILReader());
        Locals = DecodeLocals(loader, block);
        ExceptionClauses = DecodeExceptionClauses(loader, block);
        MaxStack = block.MaxStack;
        InitLocals = block.LocalVariablesInitialized;
    }

    public ILMethodBody() { }

    private static ILInstruction[] DecodeInsts(ModuleLoader loader, BlobReader reader)
    {
        // Doing two passes over the raw data is slightly more efficient than using a list,
        // since it avoids extra allocations and copies due to resizes.
        // (Though we may want to take this out if we ever decide to support invalid/obfuscated IL.)
        var insts = new ILInstruction[CountInsts(reader)];
        for (int i = 0; i < insts.Length; i++) {
            insts[i] = DecodeInst(loader, ref reader);
        }
        return insts;
    }

    private static int CountInsts(BlobReader reader)
    {
        int count = 0;
        while (reader.Offset < reader.Length) {
            var opcode = DecodeOpcode(ref reader);

            int operSize = opcode switch {
                ILCode.Switch => reader.ReadInt32() * 4,
                _ => opcode.GetOperandType().GetSize()
            };
            reader.Offset += operSize;
            count++;
        }
        return count;
    }

    private static ILInstruction DecodeInst(ModuleLoader loader, ref BlobReader reader)
    {
        var inst = new ILInstruction() {
            Offset = reader.Offset,
            OpCode = DecodeOpcode(ref reader)
        };
        inst.Operand = inst.OperandType switch {
            ILOperandType.BrTarget => reader.ReadInt32() + reader.Offset,
            ILOperandType.Field or
            ILOperandType.Method or
            ILOperandType.Tok or
            ILOperandType.Type
                => loader.GetEntity(MetadataTokens.EntityHandle(reader.ReadInt32())),
            ILOperandType.Sig
                // TODO: fix generic context (reuse generic parameters)
                => loader.DecodeMethodSig(MetadataTokens.StandaloneSignatureHandle(reader.ReadInt32())),
            ILOperandType.String => loader._reader.GetUserString(MetadataTokens.UserStringHandle(reader.ReadInt32())),
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
        return inst;

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

    private static ILCode DecodeOpcode(ref BlobReader reader)
    {
        int b = reader.ReadByte();
        return (ILCode)(b == 0xFE ? 0xFE00 | reader.ReadByte() : b);
    }

    private static ExceptionClause[] DecodeExceptionClauses(ModuleLoader loader, MethodBodyBlock block)
    {
        if (block.ExceptionRegions.Length == 0) {
            return [];
        }
        var regions = new ExceptionClause[block.ExceptionRegions.Length];

        for (int i = 0; i < regions.Length; i++) {
            var region = block.ExceptionRegions[i];

            regions[i] = new() {
                Kind = region.Kind,
                CatchType = region.CatchType.IsNil ? null : (TypeDefOrSpec)loader.GetEntity(region.CatchType),
                HandlerStart = region.HandlerOffset,
                HandlerEnd = region.HandlerOffset + region.HandlerLength,
                TryStart = region.TryOffset,
                TryEnd = region.TryOffset + region.TryLength,
                FilterStart = region.FilterOffset
            };
        }
        return regions;
    }

    private static ILVariable[] DecodeLocals(ModuleLoader loader, MethodBodyBlock block)
    {
        if (block.LocalSignature.IsNil) {
            return [];
        }
        var info = loader._reader.GetStandaloneSignature(block.LocalSignature);
        return new SignatureDecoder(loader, info.Signature).DecodeLocals();
    }
}

public class ILVariable
{
    public TypeDesc Type { get; set; }
    public int Index { get; set; }
    public bool IsPinned { get; set; }

    public ILVariable(TypeDesc type, int index, bool isPinned = false)
    {
        Type = type;
        Index = index;
        IsPinned = isPinned;
    }

    public override string ToString() => $"V_{(Index < 0 ? "?" : Index.ToString())}({Type}{(IsPinned ? "^" : "")})";
}
public class ExceptionClause
{
    public ExceptionRegionKind Kind { get; set; }

    /// <summary> The catch type if the region represents a catch handler, or null otherwise. </summary>
    public TypeDefOrSpec? CatchType { get; set; }

    /// <summary> Gets the starting IL offset of the exception handler. </summary>
    public int HandlerStart { get; set; }
    /// <summary> Gets the ending IL offset of the exception handler. </summary>
    public int HandlerEnd { get; set; }

    /// <summary> Gets the starting IL offset of the try region. </summary>
    public int TryStart { get; set; }
    /// <summary> Gets the ending IL offset of the try region. </summary>
    public int TryEnd { get; set; }

    /// <summary> Gets the starting IL offset of the filter region, or -1 if the region is not a filter. </summary>
    public int FilterStart { get; set; } = -1;
    /// <summary> Gets the ending IL offset of the filter region. This is an alias for <see cref="HandlerStart"/>. </summary>
    public int FilterEnd => HandlerStart;

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append($"Try(IL_{TryStart:X4}-IL_{TryEnd:X4}) ");
        sb.Append(Kind);
        if (Kind == ExceptionRegionKind.Catch) {
            sb.Append($"<{CatchType}>");
        }
        sb.Append($"(IL_{HandlerStart:X4}-IL_{HandlerEnd:X4})");
        if (FilterStart >= 0) {
            sb.Append($" Filter IL_{FilterStart:X4}");
        }
        return sb.ToString();
    }
}
