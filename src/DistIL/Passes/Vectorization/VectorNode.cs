namespace DistIL.Passes.Vectorization;

using DistIL.IR.Utils;

internal abstract class VectorNode
{
    public required VectorType Type;

    public abstract Value Emit(IRBuilder builder, VectorFuncTable table);
}

internal class PackNode : VectorNode
{
    public required Value[] Args;

    public override Value Emit(IRBuilder builder, VectorFuncTable table)
        => table.BuildCall(builder, Type, "Create:N", Args);
}
internal class ScalarNode : VectorNode
{
    public required Value Arg;
    public bool IsSplat = true; //Whether to splat arg in all lanes or just to the first (through CreateScalarUnsafe())

    public override Value Emit(IRBuilder builder, VectorFuncTable table)
        => table.BuildCall(builder, Type, IsSplat ? "Create:1" : "CreateScalarUnsafe:", Arg);
}

internal class LoadNode : VectorNode
{
    public required Value Address;

    public override Value Emit(IRBuilder builder, VectorFuncTable table)
        => table.BuildCall(builder, Type, "LoadUnsafe:", Address);
}

internal class ShuffleNode : VectorNode
{
    public required VectorNode Arg;
    public required int[] Indices;

    public override Value Emit(IRBuilder builder, VectorFuncTable table)
    {
        var indexType = table.GetShuffleIndexType(Type);
        var orderNode = new PackNode() {
            Type = VectorType.Create(indexType, Type.Count),
            Args = Indices.Select(x => ConstInt.Create(indexType, x)).ToArray()
        };
        var value = Arg.Emit(builder, table);
        var order = orderNode.Emit(builder, table);
        return table.BuildCall(builder, Type, "Shuffle:", value, order);
    }
}

internal class OperationNode : VectorNode
{
    public required VectorNode[] Args;
    public required VectorOp Op;

    public override Value Emit(IRBuilder builder, VectorFuncTable table)
    {
        return Op switch {
            VectorOp.Shl or
            VectorOp.Shra or
            VectorOp.Shrl
                => EmitShiftOp(builder, table),
            
            _ => EmitGenericOp(builder, table)
        };
    }

    private Value EmitGenericOp(IRBuilder builder, VectorFuncTable table)
    {
        string funcName = Op switch {
            VectorOp.Add    => "Add",
            VectorOp.Sub    => "Subtract",
            VectorOp.Mul    => "Multiply:",
            VectorOp.Div    => "Divide",

            VectorOp.And    => "BitwiseAnd",
            VectorOp.Or     => "BitwiseOr",
            VectorOp.Xor    => "Xor",

            VectorOp.Abs    => "Abs",
            VectorOp.Sqrt   => "Sqrt",

            VectorOp.Min    => "Min",
            VectorOp.Max    => "Max",

            VectorOp.Floor  => "Floor:",
            VectorOp.Ceil   => "Ceiling:",
            //TODO: Mapping for Round and Fmadd (no public xplat API)

            VectorOp.Select => "ConditionalSelect",
            VectorOp.ExtractMSB => "ExtractMostSignificantBits"
        };
        var loweredArgs = Args.Select(a => a.Emit(builder, table)).ToArray();
        return table.BuildCall(builder, Type, funcName, loweredArgs);
    }

    private Value EmitShiftOp(IRBuilder builder, VectorFuncTable table)
    {
        if (Args[1] is not ScalarNode { Arg: ConstInt shiftAmount }) {
            throw new NotSupportedException("Vector shift amount must be a constant");
        }
        string funcName = Op switch {
            VectorOp.Shl  => "ShiftLeft:",
            VectorOp.Shra => "ShiftRightArithmetic:",
            VectorOp.Shrl => "ShiftRightLogical:"
        };
        var value = Args[0].Emit(builder, table);
        return table.BuildCall(builder, Type, funcName, value, shiftAmount);
    }
}
internal enum VectorOp
{
    Add, Sub, Mul, Div,
    And, Or, Xor,
    Shl, Shra, Shrl,

    Neg, Not,

    Floor, Ceil, //Round,
    Sqrt, //Fmadd,
    Abs, Min, Max,

    Select,
    //CmpEq, CmpNe, CmpLt, CmpGt, CmpLe, CmpGe,

    //GetLane, SetLane,
    ExtractMSB
}