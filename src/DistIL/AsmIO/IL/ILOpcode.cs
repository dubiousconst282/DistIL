namespace DistIL.AsmIO;

public partial class ILCodes
{
    // Use packed bitfield for flags to avoid code bloat
    const int
        OTMask = 0x1F,      // 000000000000000000000000000XXXXX

        FCShift = 5,        // 00000000000000000000000XXXX00000
        FCMask = 0x0F,

        SBPopShift = 12,    // 000000000000000XXXXX000000000000
        SBPushShift = 17,   // 0000000000XXXXX00000000000000000
        SBMask = 0x1F,

        EndsUncondJmpBlkFlag = 0x01000000,   // 0000000X000000000000000000000000

        StackChangeShift = 28;               // XXXX0000000000000000000000000000

    const int TableSize = 0x11F;

    private static readonly int[] _flags = new int[TableSize];
    private static volatile string[]? _nameCache;

    private static int GetTableIndex(int code)
    {
        if (code <= 0xFF) {
            return code;
        } else if (code >= 0xFE00 && code <= 0xFE1E) {
            // Transform two byte opcode value to lower range that's suitable
            // for array index
            return (code - 0xFE00) + 0x100;
        }
        // Unknown opcode
        return -1;
    }

    private static void Reg(ILCode code, int flags)
    {
        _flags[GetTableIndex((int)code)] = flags;
    }

    private static int GetFlag(ILCode code, int shift, int mask)
    {
        int index = GetTableIndex((int)code);
        if (index < 0) index = 0; //use nop's flags for unknown opcodes
        return (_flags[index] >> shift) & mask;
    }

    public static ILOperandType GetOperandType(this ILCode code)
        => (ILOperandType)GetFlag(code, 0, OTMask);

    public static ILFlowControl GetFlowControl(this ILCode code)
        => (ILFlowControl)GetFlag(code, FCShift, FCMask);

    public static ILStackBehaviour GetStackBehaviourPop(this ILCode code)
        => (ILStackBehaviour)GetFlag(code, SBPopShift, SBMask);

    public static ILStackBehaviour GetStackBehaviourPush(this ILCode code)
        => (ILStackBehaviour)GetFlag(code, SBPushShift, SBMask);

    public static int GetStackChange(this ILCode code)
        => GetFlag(code, StackChangeShift, ~0);

    public static int GetSize(this ILCode code)
        => (int)code <= 0xFF ? 1 : 2;

    /// <summary> Checks whether the specified opcode terminates a basic block. </summary>
    public static bool IsTerminator(this ILCode code)
        => GetFlowControl(code) is
            ILFlowControl.Branch or
            ILFlowControl.CondBranch or
            ILFlowControl.Return or
            ILFlowControl.Throw;

    public static string GetName(this ILCode code)
    {
        int idx = GetTableIndex((int)code);
        if (idx < 0) {
            return $"unk.{(int)code:x4}";
        }
        // Create and cache the opcode names lazily. They should be rarely used (only for logging, etc.)
        // Note that we don't use any locks here because we always get the same names. The last one wins.
        _nameCache ??= new string[TableSize];
        string name = _nameCache[idx];
        if (name == null) {
            // Create ilasm style name from the enum value name.
            name = code.ToString().ToLowerInvariant().Replace('_', '.');
            _nameCache[idx] = name;
        }
        return name;
    }
}

public enum ILOperandType
{
    BrTarget = 0,
    Field = 1,
    I = 2,
    I8 = 3,
    Method = 4,
    None = 5,
    R = 7,
    Sig = 9,
    String = 10,
    Switch = 11,
    Tok = 12,
    Type = 13,
    Var = 14,
    ShortBrTarget = 15,
    ShortI = 16,
    ShortR = 17,
    ShortVar = 18,
}
public enum ILFlowControl
{
    Branch = 0,
    Break = 1,
    Call = 2,
    CondBranch = 3,
    Meta = 4,
    Next = 5,
    Return = 7,
    Throw = 8,
}
public enum ILStackBehaviour
{
    Pop0 = 0,
    Pop1 = 1,
    Pop1_pop1 = 2,
    Popi = 3,
    Popi_pop1 = 4,
    Popi_popi = 5,
    Popi_popi8 = 6,
    Popi_popi_popi = 7,
    Popi_popr4 = 8,
    Popi_popr8 = 9,
    Popref = 10,
    Popref_pop1 = 11,
    Popref_popi = 12,
    Popref_popi_popi = 13,
    Popref_popi_popi8 = 14,
    Popref_popi_popr4 = 15,
    Popref_popi_popr8 = 16,
    Popref_popi_popref = 17,
    Push0 = 18,
    Push1 = 19,
    Push1_push1 = 20,
    Pushi = 21,
    Pushi8 = 22,
    Pushr4 = 23,
    Pushr8 = 24,
    Pushref = 25,
    Varpop = 26,
    Varpush = 27,
    Popref_popi_pop1 = 28,
}