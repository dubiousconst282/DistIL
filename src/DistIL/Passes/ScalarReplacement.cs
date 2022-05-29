namespace DistIL.Passes;

using DistIL.IR;

/// <summary> Inline object/structs into local variables. aka "Scalar Replacement of Aggregates" </summary>
public class ScalarReplacement : MethodPass
{
    public override void Run(MethodTransformContext ctx)
    {
        var objs = new Dictionary<Value, ObjectInfo>();

        //Analyze method (collect accesses and whether object escapes)
        foreach (var rawInst in ctx.Method.Instructions()) {
            switch (rawInst) {
                //Collect new objects
                case NewObjInst inst: {
                    if (IsDefaultCtor(inst)) {
                        objs.Add(inst, new ObjectInfo() { Object = rawInst });
                    }
                    break;
                }
                //Collect member accesses
                case FieldAccessInst inst: {
                    if (inst.IsInstance && objs.TryGetValue(inst.Obj, out var info)) {
                        info.Accesses.Add(inst);
                    }
                    break;
                }
                //Mark operands of unhandled instructions as escaping
                default: {
                    foreach (var oper in rawInst.Operands) {
                        if (objs.TryGetValue(oper, out var info)) {
                            info.Escapes = true;
                        }
                    }
                    break;
                }
            }
        }

        //Replace with scalars
        int objId = 1;
        foreach (var (obj, info) in objs) {
            if (info.Escapes) continue;

            foreach (var access in info.Accesses) {
                var field = access.Field;
                if (!info.Scalars.TryGetValue(field, out var variable)) {
                    variable = new Variable(field.Type, name: $"sroa{objId}_{field.Name}");
                    //TODO: initialize var
                    var initSt = new StoreVarInst(variable, ConstInt.CreateI(0));
                    initSt.InsertAfter(info.Object as NewObjInst);
                    info.Scalars[field] = variable;
                }
                if (access is LoadFieldInst) {
                    access.ReplaceWith(new LoadVarInst(variable));
                } else if (access is StoreFieldInst st) {
                    access.ReplaceWith(new StoreVarInst(variable, st.Value));
                }
            }
            if (obj is NewObjInst nwo) {
                nwo.Remove();
            }
            objId++;
        }
    }

    private bool IsDefaultCtor(NewObjInst inst)
    {
        return true; //TODO
    }

    class ObjectInfo
    {
        public Value Object = null!; //Variable or Instruction
        public List<FieldAccessInst> Accesses = new(); //List of instructions accessing Object. e.g. LoadFieldInst/StoreFieldInst
        public bool Escapes = false; //Whether Object escapes (e.g. used as a call argument or stored in an array)
        public Dictionary<FieldDesc, Variable> Scalars = new(); //Map of Field -> Local
    }
}