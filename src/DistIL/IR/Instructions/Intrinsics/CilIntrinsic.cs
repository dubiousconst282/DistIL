namespace DistIL.IR;

public abstract class CilIntrinsic : IntrinsicInst
{
    public abstract ILCode Opcode { get; }

    public override string Namespace => "CIL";
    public override string Name => GetType().Name;

    protected CilIntrinsic(TypeDesc resultType, Value[] args) : base(resultType, args) { }
    protected CilIntrinsic(TypeDesc resultType, EntityDesc[] staticArgs, Value[] args) : base(resultType, staticArgs, args) { }

    /// <summary> T[] NewArray&lt;T&gt;(nint length) </summary>
    public class NewArray(TypeDesc elemType, Value length) : CilIntrinsic(elemType.CreateArray(), [length])
    {
        public override ILCode Opcode => ILCode.Newarr;
        public override bool MayThrow => true;
        public TypeDesc ElemType => ResultType.ElemType!;
    }

    /// <summary> nuint ArrayLen(Array arr) </summary>
    public class ArrayLen(Value array) : CilIntrinsic(PrimType.UIntPtr, [array])
    {
        public override ILCode Opcode => ILCode.Ldlen;
        public override bool MayThrow => true;
    }

    /// <summary> T CastClass&lt;T&gt;(object obj) </summary>
    public class CastClass(TypeDesc destType, Value obj) : CilIntrinsic(destType, [obj])
    {
        public override ILCode Opcode => ILCode.Castclass;
        public override bool MayThrow => true;
        public TypeDesc DestType => ResultType;
    }

    /// <summary> (object?)T AsInstance&lt;T&gt;(object obj) </summary>
    public class AsInstance(TypeDesc destType, Value obj)
        : CilIntrinsic(destType.IsValueType ? PrimType.Object : destType, [destType], [obj])
    {
        public override ILCode Opcode => ILCode.Isinst;
        public TypeDesc DestType => (TypeDesc)StaticArgs[0];
    }

    /// <summary> (object)T Box&lt;T&gt;(T val) </summary>
    public class Box(TypeDesc type, Value val) : CilIntrinsic(PrimType.Object, [type], [val])
    {
        public override ILCode Opcode => ILCode.Box;
        public TypeDesc SourceType => (TypeDesc)StaticArgs[0];
    }

    /// <summary> T UnboxObj&lt;T&gt;(object obj) </summary>
    public class UnboxObj(TypeDesc type, Value obj) : CilIntrinsic(type, [obj])
    {
        public override ILCode Opcode => ILCode.Unbox_Any;
        public override bool MayThrow => true;
        public override bool MayReadFromMemory => true;
        public TypeDesc DestType => ResultType;
    }

    /// <summary> T readonly&amp; UnboxRef&lt;T&gt;(object obj) </summary>
    public class UnboxRef(TypeDesc type, Value obj) : CilIntrinsic(type.CreateByref(), [obj])
    {
        public override ILCode Opcode => ILCode.Unbox;
        public override bool MayThrow => true;
        public TypeDesc DestType => ResultType.ElemType!;
    }

    /// <summary>
    /// void MemCopy(void* dest, void* src, uint numBytes) <br/>
    /// void MemCopy&lt;T&gt;(void* dest, void* src)
    /// </summary>
    public class MemCopy : CilIntrinsic
    {
        public override ILCode Opcode => StaticArgs.Length > 0 ? ILCode.Cpobj : ILCode.Cpblk;
        public PointerFlags Flags { get; set; }

        public override bool MayThrow => true;
        public override bool MayWriteToMemory => true;
        public override bool MayReadFromMemory => true;

        public MemCopy(Value destPtr, Value srcPtr, Value numBytes, PointerFlags flags = 0)
            : base(PrimType.Void, [destPtr, srcPtr, numBytes]) { Flags = flags; }

        public MemCopy(Value destPtr, Value srcPtr, TypeDesc type, PointerFlags flags = 0)
            : base(PrimType.Void, [type], [destPtr, srcPtr]) { Flags = flags; }
    }

    /// <summary>
    /// void MemSet(void* ptr, byte value, uint numBytes)  <br/>
    /// void MemSet&lt;T&gt;(void* ptr)
    /// </summary>
    public class MemSet : CilIntrinsic
    {
        public override ILCode Opcode => StaticArgs.Length > 0 ? ILCode.Initobj : ILCode.Initblk;
        public PointerFlags Flags { get; set; }

        public override bool MayThrow => true;
        public override bool MayWriteToMemory => true;

        public MemSet(Value destPtr, Value value, Value numBytes, PointerFlags flags = 0)
            : base(PrimType.Void, [destPtr, value, numBytes]) { Flags = flags; }

        public MemSet(Value destPtr, TypeDesc type, PointerFlags flags = 0)
            : base(PrimType.Void, [type], [destPtr]) { Flags = flags; }
    }

    /// <summary> int SizeOf&lt;T&gt;() </summary>
    public class SizeOf(TypeDesc type) : CilIntrinsic(PrimType.Int32, [type], [])
    {
        public override ILCode Opcode => ILCode.Sizeof;
        public TypeDesc ObjType => (TypeDesc)StaticArgs[0];
    }

    /// <summary> void* Alloca(nuint numBytes) </summary>
    public class Alloca(Value numBytes) : CilIntrinsic(PrimType.Void.CreatePointer(), [numBytes])
    {
        public override ILCode Opcode => ILCode.Localloc;
        public override bool MayThrow => true;
    }

    /// <summary>
    /// RuntimeTypeHandle LoadHandle(TypeDesc type) <br/>
    /// RuntimeMethodHandle LoadHandle(MethodDesc method) <br/>
    /// RuntimeFieldHandle LoadHandle(FieldDesc field)
    /// </summary>
    public class LoadHandle : CilIntrinsic
    {
        public override ILCode Opcode => ILCode.Ldtoken;
        public EntityDesc Entity => StaticArgs[0];

        public LoadHandle(ModuleResolver resolver, EntityDesc entity)
            : base(PrimType.Void, [entity], [])
        {
            var sys = resolver.SysTypes;
            ResultType = entity switch {
                TypeDesc => sys.RuntimeTypeHandle,
                MethodDesc => sys.RuntimeMethodHandle,
                FieldDesc => sys.RuntimeFieldHandle,
                _ => throw new ArgumentException("Invalid argument for LoadHandle/ldtoken")
            };
        }
    }

    /// <summary> float CheckFinite(float x) </summary>
    public class CheckFinite(Value value) : CilIntrinsic(value.ResultType, [value])
    {
        public override ILCode Opcode => ILCode.Ckfinite;
        public override bool MayThrow => true;
    }
}