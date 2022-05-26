namespace DistIL.IR;

using System.Globalization;

public class ConstFloat : Const
{
    private double _value;
    public double Value {
        get => _value;
        set => _value = IsSingle ? (float)value : value;
    }
    public bool IsSingle => ResultType.Kind == TypeKind.Single;
    public bool IsDouble => ResultType.Kind == TypeKind.Double;

    private ConstFloat() { }

    public static ConstFloat CreateS(float value) => Create(PrimType.Single, value);
    public static ConstFloat CreateD(double value) => Create(PrimType.Double, value);

    public static ConstFloat Create(TypeDesc type, double value)
    {
        Ensure(type.StackType == StackType.Float);
        return new ConstFloat() { ResultType = type, Value = value };
    }

    public override void Print(StringBuilder sb, SlotTracker slotTracker)
    {
        string str = Value.ToString(CultureInfo.InvariantCulture);
        sb.Append(str);
        if (!str.Contains('.')) sb.Append(".0");
        if (IsSingle) sb.Append("f");
    }

    public override bool Equals(Const? other) => other is ConstFloat o && o.Value.Equals(Value) && o.ResultType == ResultType;
    public override int GetHashCode() => Value.GetHashCode();
}