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
    
    void Visit(LoadInst inst);
    void Visit(StoreInst inst);
    
    void Visit(ArrayAddrInst inst);
    void Visit(FieldAddrInst inst);
    void Visit(PtrOffsetInst inst);

    void Visit(CallInst inst);
    void Visit(NewObjInst inst);
    void Visit(FuncAddrInst inst);
    void Visit(IntrinsicInst inst);
    void Visit(SelectInst inst);

    void Visit(ReturnInst inst);
    void Visit(BranchInst inst);
    void Visit(SwitchInst inst);

    void Visit(PhiInst inst);

    void Visit(GuardInst inst);
    void Visit(LeaveInst inst);
    void Visit(ResumeInst inst);
    void Visit(ThrowInst inst);
}