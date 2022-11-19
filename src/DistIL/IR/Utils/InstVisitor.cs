namespace DistIL.IR;

public interface InstVisitor
{
    void Visit(BinaryInst inst);
    void Visit(UnaryInst inst);
    void Visit(CompareInst inst);
    void Visit(ConvertInst inst);

    void Visit(LoadVarInst inst);
    void Visit(StoreVarInst inst);
    void Visit(VarAddrInst inst);
    
    void Visit(LoadPtrInst inst);
    void Visit(StorePtrInst inst);

    void Visit(ArrayLenInst inst);
    void Visit(LoadArrayInst inst);
    void Visit(StoreArrayInst inst);
    void Visit(ArrayAddrInst inst);

    void Visit(LoadFieldInst inst);
    void Visit(StoreFieldInst inst);
    void Visit(FieldAddrInst inst);

    void Visit(CallInst inst);
    void Visit(NewObjInst inst);
    void Visit(FuncAddrInst inst);
    void Visit(IntrinsicInst inst);

    void Visit(ReturnInst inst);
    void Visit(BranchInst inst);
    void Visit(SwitchInst inst);

    void Visit(PhiInst inst);

    void Visit(GuardInst inst);
    void Visit(LeaveInst inst);
    void Visit(ResumeInst inst);
    void Visit(ThrowInst inst);
}