namespace DistIL.Analysis;

using DistIL.IR.Utils;

/// <summary>
/// Parses and provides information about a high-level loop structure.
/// </summary>
public class ShapedLoopInfo
{
    public LoopInfo Loop { get; }

    public BasicBlock Header => Loop.Header;
    public RefSet<BasicBlock> Blocks => Loop.Blocks;

    public Instruction ExitCondition { get; }
    public BasicBlock PreHeader { get; private set; } = null!;
    public BasicBlock Latch { get; private set; } = null!;
    public BasicBlock Exit { get; private set; } = null!;

    protected ShapedLoopInfo(LoopInfo loop, Instruction exitCond)
    {
        Loop = loop;
        ExitCondition = exitCond;
    }

    /// <inheritdoc cref="LoopInfo.Contains(BasicBlock)"/>
    public bool Contains(BasicBlock block) => Loop.Contains(block);
    
    /// <inheritdoc cref="LoopInfo.IsInvariant(Value)"/>
    public bool IsInvariant(Value value) => Loop.IsInvariant(value);

    /// <summary> Checks if the loop has a known trip count at the preheader, or if it is possible to calculate it before the specified instruction. </summary>
    public virtual bool HasKnownTripCount(DominatorTree? domTree, Instruction? position) => false;

    /// <summary> Gets or calculates the loop trip count if possible. </summary>
    /// <remarks> UB if called when <c>HasKnownTripCount() != true</c>. </remarks>
    /// <param name="builder"> An optional builder where the trip count calculation code will be emitted. </param>
    public virtual Value? GetTripCount(IRBuilder? builder) => null;

    public static ShapedLoopInfo? Parse(LoopInfo loop)
    {
        var exitCond = loop.GetExitCondition();

        var shape = exitCond switch {
            CompareInst { Op: CompareOp.Slt or CompareOp.Ult }
                => new CountingLoopInfo(loop, exitCond),

            CallInst { Method.Name: "MoveNext" }
                => new EnumeratingLoopInfo(loop, exitCond),

            _ => null as ShapedLoopInfo
        };

        return shape != null && shape.Parse() ? shape : null;
    }

    protected virtual bool Parse()
    {
        PreHeader = Loop.GetPreheader()!;
        Latch = Loop.GetLatch()!;

        // GetExit() is expansive because it will do a full scan over all loop blocks.
        // Since we know that ExitCond exists, the header must have a branch to the exit block.
        Exit = ((BranchInst)Loop.Header.Last).Else!;
        Debug.Assert(Exit == Loop.GetExit());

        return PreHeader != null && Latch != null;
    }
}

/// <summary>
/// Shape info about a counting for loop:
/// <c>for (i = Start; i &lt; End; i += Stride) { ... }</c>
/// </summary>
public class CountingLoopInfo : ShapedLoopInfo
{
    public new CompareInst ExitCondition => (CompareInst)base.ExitCondition;

    public PhiInst Counter { get; private set; } = null!;
    public Instruction UpdatedCounter { get; private set; } = null!;
    public Value Start => Counter.GetValue(PreHeader);
    public Value End => ExitCondition.Right;

    /// <summary> Checks if the loop counter starts at 0 and increments by 1 on every iteration. </summary>
    /// <remarks> This may evaluate to true even if the loop bound (<see cref="End"/>) is not invariant. </remarks>
    public bool IsCanonical =>
        Start is ConstInt { Value: 0 } &&
        UpdatedCounter is BinaryInst { Op: BinaryOp.Add, Right: ConstInt { Value: 1 } };

    internal CountingLoopInfo(LoopInfo loop, Instruction exitCond) : base(loop, exitCond) { }

    /// <summary> Returns the loop counter step. int | TypeDesc | null. </summary>
    public object? GetStride()
    {
        if (UpdatedCounter is BinaryInst { Op: BinaryOp.Add, Right: ConstInt v } bin && bin.Left == Counter) {
            return checked((int)v.Value);
        }
        if (UpdatedCounter is PtrOffsetInst { Index: ConstInt { Value: 1 } } lea && lea.BasePtr == Counter) {
            return lea.KnownStride ? lea.Stride : lea.ElemType;
        }
        return null;
    }

    /// <summary> Returns the source array of which this loop is iterating over, based on the exit condition. </summary>
    public Value? GetSourceArray()
    {
        return End is ConvertInst { Value: CilIntrinsic.ArrayLen { Args: [var array] }, DestType: TypeKind.Int32 } &&
               Loop.IsInvariant(array) ? array : null;
    }

    public override bool HasKnownTripCount(DominatorTree? domTree, Instruction? position)
    {
        // SCEVs could help in computing trip count for more complicated loops,
        // but probably not worth much for us
        if (!IsCanonical) return false;

        var sourceOrBound = GetSourceArray() ?? End;
        if (!Loop.IsInvariant(sourceOrBound)) return false;

        if (domTree != null && position != null && sourceOrBound is Instruction sourceOrBoundI) {
            return domTree.Dominates(sourceOrBoundI, position);
        }
        return true;
    }
    public override Value? GetTripCount(IRBuilder? builder)
    {
        var array = GetSourceArray();
        if (array != null) {
            return builder?.CreateConvert(builder.CreateArrayLen(array), PrimType.Int32);
        }
        return End;
    }

    protected override bool Parse()
    {
        if (!base.Parse()) return false;
        if (ExitCondition.Op is not (CompareOp.Slt or CompareOp.Ult)) return false;

        // BB_Header:
        //   Counter = phi [BB_PreHeader: Start], [BB_Latch: UpdatedCounter]
        // BB_BodyOrLatch:
        //   UpdatedCounter = add Counter, Stride |
        //   UpdatedCounter = lea Counter + 1 * sizeof(T)
        Counter = (ExitCondition.Left as PhiInst)!;
        if (Counter == null || Counter.Block != Loop.Header) return false;

        UpdatedCounter = (Counter.GetValue(Latch) as Instruction)!;
        if (GetStride() == null) return false;

        return true;
    }
}

/// <summary>
/// Shape info about an enumerator loop:
/// <c>while (itr.MoveNext()) { var item = itr.get_Current(); ... }</c>
/// </summary>
public class EnumeratingLoopInfo : ShapedLoopInfo
{
    /// <summary> The MoveNext() call. </summary>
    public new CallInst ExitCondition => (CallInst)base.ExitCondition;

    /// <summary> The get_Current() call. </summary>
    public CallInst CurrentItem { get; private set; } = null!;

    public Value Enumerator => ExitCondition.Args[0];

    internal EnumeratingLoopInfo(LoopInfo loop, Instruction exitCond) : base(loop, exitCond) { }

    public override bool HasKnownTripCount(DominatorTree? domTree, Instruction? position)
    {
        if (domTree == null || position == null) return false;
        if (Enumerator is not CallInst { Method.Name: "GetEnumerator", Args: [var source] }) return false;

        var resolver = position.Block.Method.Definition.Module.Resolver;
        var t_Collection = resolver.Import(typeof(ICollection<>)).GetSpec([CurrentItem.ResultType]);
        var t_ROCollection = resolver.Import(typeof(IReadOnlyCollection<>)).GetSpec([CurrentItem.ResultType]);

        // IROCollection<T> doesn't inherit from ICollection<T> as it should, for backwards compat or whatever,
        // so we must check for both.
        if (!source.ResultType.Inherits(t_Collection) && !source.ResultType.Inherits(t_ROCollection)) return false;

        if (source is Instruction sourceI && !domTree.Dominates(sourceI, position)) return false;

        return true;
    }
    public override Value? GetTripCount(IRBuilder? builder)
    {
        if (builder == null) return null;
        if (Enumerator is not CallInst { Method.Name: "GetEnumerator", Args: [var source] }) return null;

        return builder.CreateCallVirt("get_Count", source);
    }

    protected override bool Parse()
    {
        if (!base.Parse()) return false;

        if (ExitCondition is not { Method.Name: "MoveNext", NumArgs: 1, ResultType.Kind: TypeKind.Bool }) return false;

        CurrentItem = (((BranchInst)Header.Last).Then.First as CallInst)!;
        if (CurrentItem is not { Method.Name: "get_Current", NumArgs: 1, HasResult: true }) return false;
        if (CurrentItem.Args[0] != Enumerator) return false;

        // TODO: consider checking that Enumerator is only used twice inside the loop

        return true;
    }
}