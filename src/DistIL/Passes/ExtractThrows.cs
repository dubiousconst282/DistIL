namespace DistIL.Passes;

using System.Reflection;

/// <summary> Extract blocks ending with throw to helper methods. </summary>
public class ExtractThrows : IMethodPass
{
    readonly Dictionary<MethodDesc, MethodDef> _thCache = new();

    public MethodPassResult Run(MethodTransformContext ctx)
    {
        bool changed = false;

        foreach (var block in ctx.Method) {
            if (block.Last is not ThrowInst { Exception: NewObjInst excAlloc } throwInst) continue;

            // If the block ends with exactly "throw new Exception()", try to replace it with a helper call.
            if (throwInst.Prev == excAlloc && excAlloc.Args is [] or [ConstString]) {
                string? msg = excAlloc.Args is [ConstString msgConst] ? msgConst.Value : null;
                var (helper, msgId) = FindSharedThrowHelper(ctx.Compilation, excAlloc.Constructor, msg);

                var helperCall = new CallInst(helper, msg == null ? [] : [ConstInt.CreateI(msgId)]);
                helperCall.InsertBefore(throwInst);

                // "throw null" seems to work well for this case because RyuJIT knows about non-returning methods
                // (as long as they're inline-able) and it can properly eliminate the throw.
                // An alternative would be to fallthrough some arbitrary block (maybe implicitly with a new UnreachableInst),
                // but that could mess up the CFG too much and confuse some opts or the JIT.
                throwInst.Exception = ConstNull.Create();
                excAlloc.Remove();
            }
        }
        return changed ? MethodInvalidations.DataFlow : MethodInvalidations.None;
    }

    private (MethodDef Helper, int MsgId) FindSharedThrowHelper(Compilation comp, MethodDesc ctor, string? msg)
    {
        if (!_thCache.TryGetValue(ctor, out var helper)) {
            // TODO: handle name collisions
            helper = comp.GetAuxType().CreateMethod(
                "TH_" + ctor.DeclaringType.Name, PrimType.Void,
                msg == null ? [] : [new ParamDef(PrimType.UInt32, "msgId")],
                MethodAttributes.Assembly | MethodAttributes.Static);

            helper.ILBody = new ILMethodBody() {
                Instructions = GenerateThrowHelperIL(comp, ctor, msg != null),
                MaxStack = 8,
            };

            _thCache.Add(ctor, helper);
        }
        int msgId = 0;

        if (msg != null) {
            ref var ldstr = ref helper.ILBody!.Instructions.AsSpan()[0];
            string str = (string)ldstr.Operand!;
            int offset = str.IndexOf(msg);

            // TODO: change this method to return failure instead of throwing when msg len is >= 256
            Ensure.That(msg.Length < 256 && str.Length < (1 << 24) - 256);

            if (offset < 0) {
                offset = str.Length;
                ldstr.Operand = str + msg + "\n\n";
            }
            msgId = (offset << 8) | msg.Length;
        }
        return (helper, msgId);
    }

    private static ILInstruction[] GenerateThrowHelperIL(Compilation comp, MethodDesc ctor, bool hasConstMsg)
    {
        ILInstruction[] code= [
            new(ILCode.Newobj, ctor),
            new(ILCode.Throw),
        ];

        if (hasConstMsg) {
            var m_Substr = comp.Resolver.Import(typeof(string))
                                        .FindMethod("Substring", new MethodSig(PrimType.String, [PrimType.Int32, PrimType.Int32]));

            // Extract from a substring containing all messages, indexed by a pair of bit-packed (offset: 24, len: 8).
            // This makes for very small IL but trades off runtime cost.
            // "abcdef".Substring(msgId >>> 8, msgId & 255)
            code = [
                // s0: "concatenated messages"
                new(ILCode.Ldstr, ""),
                // s1: msgId >>> 8
                new(ILCode.Ldarg_0),
                new(ILCode.Ldc_I4_8),
                new(ILCode.Shr_Un),
                // s2: msgId & 255
                new(ILCode.Ldarg_0),
                new(ILCode.Ldc_I4, 255),
                new(ILCode.And),
                // s0: Substring(s0, s1, s2)
                new(ILCode.Call, m_Substr),
                // throw(newobj Exception::.ctor(s0))
                new(ILCode.Newobj, ctor),
                new(ILCode.Throw),
            ];
        }

        // Compute offsets
        // TODO: consider moving this out to ILMethodBody
        int offset = 0;
        foreach (ref var inst in code.AsSpan()) {
            inst.Offset = offset;
            offset += inst.GetSize();
        }
        return code;
    }
}