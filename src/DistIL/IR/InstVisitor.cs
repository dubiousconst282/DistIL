namespace DistIL.IR;

public interface InstVisitor
{
    void Visit(BinaryInst inst) => VisitDefault(inst);
    void Visit(UnaryInst inst) => VisitDefault(inst);
    void Visit(CompareInst inst) => VisitDefault(inst);
    void Visit(ConvertInst inst) => VisitDefault(inst);

    void Visit(LoadVarInst inst) => VisitDefault(inst);
    void Visit(StoreVarInst inst) => VisitDefault(inst);
    void Visit(VarAddrInst inst) => VisitDefault(inst);
    
    void Visit(LoadPtrInst inst) => VisitDefault(inst);
    void Visit(StorePtrInst inst) => VisitDefault(inst);

    void Visit(ArrayLenInst inst) => VisitDefault(inst);
    void Visit(LoadArrayInst inst) => VisitDefault(inst);
    void Visit(StoreArrayInst inst) => VisitDefault(inst);
    void Visit(ArrayAddrInst inst) => VisitDefault(inst);

    void Visit(LoadFieldInst inst) => VisitDefault(inst);
    void Visit(StoreFieldInst inst) => VisitDefault(inst);
    void Visit(FieldAddrInst inst) => VisitDefault(inst);

    void Visit(CallInst inst) => VisitDefault(inst);
    void Visit(NewObjInst inst) => VisitDefault(inst);
    void Visit(FuncAddrInst inst) => VisitDefault(inst);
    void Visit(IntrinsicInst inst) => VisitDefault(inst);

    void Visit(ReturnInst inst) => VisitDefault(inst);
    void Visit(BranchInst inst) => VisitDefault(inst);
    void Visit(SwitchInst inst) => VisitDefault(inst);

    void Visit(PhiInst inst) => VisitDefault(inst);

    void Visit(GuardInst inst) => VisitDefault(inst);
    void Visit(LeaveInst inst) => VisitDefault(inst);
    void Visit(ContinueInst inst) => VisitDefault(inst);
    void Visit(ThrowInst inst) => VisitDefault(inst);

    void VisitDefault(Instruction inst) { }
}