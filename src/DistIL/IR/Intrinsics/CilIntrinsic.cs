namespace DistIL.IR.Intrinsics;

public class CilIntrinsic : IntrinsicDesc
{
    public override string Namespace => "CIL";
    public override string Name => Id.ToString();

    public CilIntrinsicId Id { get; private init; }
    public ILCode Opcode { get; private init; }

    public static readonly CilIntrinsic
        //T[] NewArray<T>(nint numElems)
        NewArray = new() {
            Id = CilIntrinsicId.NewArray,
            Opcode = ILCode.Newarr,
            ParamTypes = ImmutableArray.Create(s_TypePar, PrimType.IntPtr),
            ReturnType = s_Typeof0.CreateArray()
        },
        //T CastClass<T>(object obj)
        CastClass = new() {
            Id = CilIntrinsicId.CastClass,
            Opcode = ILCode.Castclass,
            ParamTypes = ImmutableArray.Create(s_TypePar, PrimType.Object),
            ReturnType = s_Typeof0
        },
        //T? AsInstance<T>(object obj)
        AsInstance = new() {
            Id = CilIntrinsicId.AsInstance,
            Opcode = ILCode.Isinst,
            ParamTypes = ImmutableArray.Create(s_TypePar, PrimType.Object),
            ReturnType = s_Typeof0
        },
        //object Box<T>(T obj)
        Box = new() {
            Id = CilIntrinsicId.Box,
            Opcode = ILCode.Box,
            ParamTypes = ImmutableArray.Create(s_TypePar, s_Typeof0),
            ReturnType = PrimType.Object
        },
        //T readonly& UnboxRef<T>(object obj)
        UnboxRef = new() {
            Id = CilIntrinsicId.UnboxRef,
            Opcode = ILCode.Unbox,
            ParamTypes = ImmutableArray.Create(s_TypePar, PrimType.Object),
            ReturnType = s_Typeof0.CreateByref()
        },
        //T UnboxObj<T>(object obj)
        UnboxObj = new() {
            Id = CilIntrinsicId.UnboxObj,
            Opcode = ILCode.Unbox_Any,
            ParamTypes = ImmutableArray.Create(s_TypePar, PrimType.Object),
            ReturnType = s_Typeof0
        },
        //void InitObj<T>(void* ptr)
        InitObj = new() {
            Id = CilIntrinsicId.InitObj,
            Opcode = ILCode.Initobj,
            ParamTypes = ImmutableArray.Create(s_TypePar, PrimType.Void.CreatePointer()),
            ReturnType = PrimType.Void
        },
        //int SizeOf<T>()
        SizeOf = new() {
            Id = CilIntrinsicId.SizeOf,
            Opcode = ILCode.Sizeof,
            ParamTypes = ImmutableArray.Create(s_TypePar),
            ReturnType = PrimType.Int32
        },
        //void* Alloca(nint size)
        Alloca = new() {
            Id = CilIntrinsicId.Alloca,
            Opcode = ILCode.Localloc,
            ParamTypes = ImmutableArray.Create<TypeDesc>(PrimType.IntPtr),
            ReturnType = PrimType.Void.CreatePointer(),
        };

    public override TypeDesc GetResultType(Value[] args)
    {
        //isinst returns the boxed object instance for value types
        if (this == AsInstance && args[0] is TypeDesc { IsValueType: true }) {
            return PrimType.Object;
        }
        return base.GetResultType(args);
    }

    public static CilIntrinsic LoadHandle(ModuleResolver resolver, EntityDesc entity)
    {
        var sys = resolver.SysTypes;
        var returnType = entity switch {
            MethodDesc => sys.RuntimeMethodHandle,
            FieldDesc  => sys.RuntimeFieldHandle,
            TypeDesc   => sys.RuntimeTypeHandle,
            _ => throw new ArgumentException("Unknown entity type for LoadHandle/ldtoken")
        };
        return new() {
            Id = CilIntrinsicId.LoadHandle,
            Opcode = ILCode.Ldtoken,
            ParamTypes = ImmutableArray.Create(s_AnyType),
            ReturnType = returnType,
        };
    }
}

public enum CilIntrinsicId
{
    NewArray,
    CastClass, AsInstance,
    Box, UnboxObj, UnboxRef,
    LoadHandle,
    InitObj,
    SizeOf,
    Alloca
}