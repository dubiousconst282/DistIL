namespace DistIL.IR;

public abstract class CilIntrinsic : IntrinsicInst
{
    public abstract ILCode Opcode { get; }

    public override string Namespace => "CIL";
    public override string Name => GetType().Name;

    protected CilIntrinsic(TypeDesc resultType, params Value[] args) : base(resultType, args) { }
    protected CilIntrinsic(TypeDesc resultType, EntityDesc[] staticArgs, Value[] args) : base(resultType, staticArgs, args) { }

    /// <summary> T[] NewArray&lt;T&gt;(nint length) </summary>
    public class NewArray : CilIntrinsic
    {
        public override ILCode Opcode => ILCode.Newarr;
        public override bool MayThrow => true;
        public TypeDesc ElemType => ResultType.ElemType!;

        public NewArray(TypeDesc elemType, Value length) : base(elemType.CreateArray(), length) { }
    }

    /// <summary> nuint ArrayLen(Array arr) </summary>
    public class ArrayLen : CilIntrinsic
    {
        public override ILCode Opcode => ILCode.Ldlen;
        public override bool MayThrow => true;

        public ArrayLen(Value array) : base(PrimType.UIntPtr, array) { }
    }
    
    /// <summary> T CastClass&lt;T&gt;(object obj) </summary>
    public class CastClass : CilIntrinsic
    {
        public override ILCode Opcode => ILCode.Castclass;
        public override bool MayThrow => true;
        public TypeDesc DestType => ResultType;

        public CastClass(TypeDesc destType, Value obj) : base(destType, obj) { }
    }

    /// <summary> (object?)T AsInstance&lt;T&gt;(object obj) </summary>
    public class AsInstance : CilIntrinsic
    {
        public override ILCode Opcode => ILCode.Isinst;
        public TypeDesc DestType => (TypeDesc)StaticArgs[0];

        public AsInstance(TypeDesc destType, Value obj)
            : base(destType.IsValueType ? PrimType.Object : destType, new[] { destType }, new[] { obj }) { }
    }

    /// <summary> (object)T Box&lt;T&gt;(T val) </summary>
    public class Box : CilIntrinsic
    {
        public override ILCode Opcode => ILCode.Box;
        public TypeDesc SourceType => (TypeDesc)StaticArgs[0];

        public Box(TypeDesc type, Value val) : base(PrimType.Object, new[] { type }, new[] { val }) { }
    }

    /// <summary> T UnboxObj&lt;T&gt;(object obj) </summary>
    public class UnboxObj : CilIntrinsic
    {
        public override ILCode Opcode => ILCode.Unbox_Any;
        public override bool MayThrow => true;
        public override bool MayReadFromMemory => true;
        public TypeDesc DestType => ResultType;

        public UnboxObj(TypeDesc type, Value obj) : base(type, obj) { }
    }

    /// <summary> T readonly&amp; UnboxRef&lt;T&gt;(object obj) </summary>
    public class UnboxRef : CilIntrinsic
    {
        public override ILCode Opcode => ILCode.Unbox;
        public override bool MayThrow => true;
        public TypeDesc DestType => ResultType.ElemType!;

        public UnboxRef(TypeDesc type, Value obj) : base(type.CreateByref(), obj) { }
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
            : base(PrimType.Void, destPtr, srcPtr, numBytes) { Flags = flags; }

        public MemCopy(Value destPtr, Value srcPtr, TypeDesc type, PointerFlags flags = 0)
            : base(PrimType.Void, new[] { type }, new[] { destPtr, srcPtr }) { Flags = flags; }
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
            : base(PrimType.Void, destPtr, value, numBytes) { Flags = flags; }

        public MemSet(Value destPtr, TypeDesc type, PointerFlags flags = 0)
            : base(PrimType.Void, new[] { type }, new[] { destPtr }) { Flags = flags; }
    }

    /// <summary> int SizeOf&lt;T&gt;() </summary>
    public class SizeOf : CilIntrinsic
    {
        public override ILCode Opcode => ILCode.Sizeof;
        public TypeDesc ObjType => (TypeDesc)StaticArgs[0];

        public SizeOf(TypeDesc type) : base(PrimType.Int32, new[] { type }, Array.Empty<Value>()) { }
    }

    /// <summary> void* Alloca(nuint numBytes) </summary>
    public class Alloca : CilIntrinsic
    {
        public override ILCode Opcode => ILCode.Localloc;
        public override bool MayThrow => true;

        public Alloca(Value numBytes) : base(PrimType.Void.CreatePointer(), numBytes) { }
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
            : base(PrimType.Void, new[] { entity }, Array.Empty<Value>())
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
    public class CheckFinite : CilIntrinsic
    {
        public override ILCode Opcode => ILCode.Ckfinite;
        public override bool MayThrow => true;

        public CheckFinite(Value value) : base(value.ResultType, value) { }
    }
}