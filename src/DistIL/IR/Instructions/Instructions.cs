namespace DistIL.IR;

//TODO:
//LoadFieldInst, StoreFieldInst

//LoadArrayInst, StoreArrayInst
//LoadPtrInst, StorePtrInst
//or LoadInst, StoreInst  [array, ptr]

//VarAddrInst, MemberAddrInst(field, func)

//CastInst or reuse ConvertInst?
//ThrowInst
//LeaveInst, EndfinallyInst (or reuse leave)

//SelectInst, BitcastInt

//IntrinsicInst -> Math.*, HW vectors, Numerics.* other common BCL methods
//or `class IntrinsicFunc : CallSite`
//or IntrinsicInst : CallInst