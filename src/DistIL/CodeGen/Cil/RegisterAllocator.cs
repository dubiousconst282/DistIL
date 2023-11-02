namespace DistIL.CodeGen.Cil;

using DistIL.Analysis;
using DistIL.AsmIO;
using DistIL.IR.Utils;

/// <summary> Allocates registers for all definitions in a method. </summary>
/// <remarks> This class only performs minimal changes to the IR, such as splitting critical edges. </remarks>
public class RegisterAllocator : IPrintDecorator
{
    readonly MethodBody _method;
    readonly InterferenceGraph _interfs;

    readonly Dictionary<(TypeDesc Type, int Color), ILVariable> _registers = new();
    readonly Dictionary<BasicBlock, List<(PhiInst Dest, Value Src)>> _phiCopies = new();

    public RegisterAllocator(MethodBody method, InterferenceGraph interfs)
    {
        _method = method;
        _interfs = interfs;

        Coalesce();
        AssignColors();
    }

    // Coalesce non-interfering phi arguments and populates `_phiCopies`
    // Note that this may change the CFG, invalidating LivenessAnalysis.
    private void Coalesce()
    {
        foreach (var block in _method) {
            foreach (var phi in block.Phis()) {
                foreach (var (pred, value) in phi) {
                    if (value is Undef) continue;

                    if (value is Instruction valI) {
                        // Avoid coalescing arguments with more concrete types if they're not exclusively
                        // used by this phi, otherwise it could lead to problems with interface resolution:
                        //   List r1 = ...    use(r1)
                        //   HashSet r2 = ...   use(r2)
                        //   IEnumerable r3 = phi [... -> r1], [... -> r2]
                        bool canMerge = valI.NumUses < 2 || phi.ResultType.IsAssignableTo(valI.ResultType);

                        if (canMerge && _interfs.TryMerge(phi, valI)) continue;
                    }

                    // Once we reach here we know that `value` is either
                    // a const or an interfering/non-merged instruction.
                    // Schedule a parallel copy at the end of `pred`.
                    var actualPred = pred.SplitCriticalEdge(block);
                    var copies = _phiCopies.GetOrAddRef(actualPred) ??= new();
                    copies.Add((phi, value));
                }
            }
        }
    }

    // Assign unique colors to each node in the interference graph
    private void AssignColors()
    {
        var usedColors = new BitSet();

        // TODO: Coloring can be optimal since we're using a SSA graph (perfect elimination order)
        // Note that our graphs are currently not chordal because nodes of different types are not connected, see AddEdge().
        foreach (var (inst, node) in _interfs.GetNodes()) {
            if (node.Color != 0) continue; //already assigned

            foreach (var neighbor in _interfs.GetAdjacent(node)) {
                if (neighbor.Color != 0) {
                    usedColors.Add(neighbor.Color - 1);
                }
            }
            node.Color = usedColors.FirstUnsetIndex() + 1;
            usedColors.Clear();
        }
    }

    private ILVariable PickRegister(TypeDesc type, int color)
    {
        return _registers.GetOrAddRef((type, color))
            ??= new(type, index: _registers.Count, isPinned: false);
    }

    /// <summary> Returns the register assigned to <paramref name="def"/>. </summary>
    public ILVariable GetRegister(Instruction def)
    {
        var node = _interfs.GetNode(def);

        if (node != null) {
            return PickRegister(node.RegisterType, node.Color);
        }
        // Assume that defs without nodes don't interfere with anything. Give them a dummy register
        return PickRegister(def.ResultType, -1);
    }

    /// <summary> Returns a list of phi-associated parallel copies that must execute at the end of <paramref name="block"/>. </summary>
    public IReadOnlyList<(PhiInst Dest, Value Value)>? GetPhiCopies(BasicBlock block)
        => _phiCopies.GetValueOrDefault(block);

    void IPrintDecorator.DecorateInst(PrintContext ctx, Instruction inst)
    {
        if (inst.HasResult && _interfs.GetNode(inst) != null) {
            ctx.Print(" @" + GetRegister(inst).Index, PrintToner.Comment);
        }
        if (inst.Next == null && GetPhiCopies(inst.Block) is { } copies) {
            ctx.PrintLine();
            ctx.Print($"@({copies.Select(e => e.Dest):, $}) = ({copies.Select(e => e.Value):, $})");
        }
    }
}