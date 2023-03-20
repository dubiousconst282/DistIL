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

    public RegisterAllocator(MethodBody method)
    {
        _method = method;
        var liveness = new LivenessAnalysis(method);
        _interfs = new InterferenceGraph(method, liveness);

        Coalesce();
        AssignColors();
    }

    //Coalesce non-interfering phi arguments and populates `_phiCopies`
    //Note that this may change the CFG, invalidating LivenessAnalysis.
    private void Coalesce()
    {
        foreach (var block in _method) {
            foreach (var phi in block.Phis()) {
                foreach (var (pred, value) in phi) {
                    if (value is Undef) continue;

                    //If the value is either a const or an interfering instruction,
                    //schedule a parallel copy at the end of `pred`.
                    //
                    //We can't coalesce arguments that have a more concrete type than that of the phi, because
                    //color assignment will arbitrarily pick a type from any of the coalesced defs, and it could 
                    //cause issues with interface resolution:
                    //  List r1 = ...
                    //  HashSet r2 = ...
                    //  IEnumerable r3 = phi [... -> r1], [... -> r2]
                    if (value is not Instruction valI || !phi.ResultType.IsAssignableTo(valI.ResultType) || !_interfs.TryMerge(phi, valI)) {
                        var actualPred = pred.SplitCriticalEdge(block);
                        var copies = _phiCopies.GetOrAddRef(actualPred) ??= new();
                        copies.Add((phi, value));
                    }
                }
            }
        }
    }

    //Assign unique colors to each node in the interference graph
    private void AssignColors()
    {
        var usedColors = new BitSet();

        //TODO: Coloring can be optimal since we're using a SSA graph (perfect elimination order)
        foreach (var (inst, node) in _interfs.GetNodes()) {
            if (node.Color != 0) continue; //already assigned

            foreach (var neighbor in _interfs.GetAdjacent(node)) {
                if (neighbor.Color != 0) {
                    usedColors.Add(neighbor.Color - 1);
                }
            }
            node.Color = usedColors.FirstUnsetIndex() + 1;
            node.Register = PickRegister(inst.ResultType, node.Color);
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
        => _interfs.GetNode(def)?.Register 
            ?? PickRegister(def.ResultType, -1); //defs with no nodes don't interfere with anything, just give them a dummy register

    /// <summary> Returns a list of phi-associated parallel copies that must execute at the end of <paramref name="block"/>. </summary>
    public IReadOnlyList<(PhiInst Dest, Value Value)>? GetPhiCopies(BasicBlock block)
        => _phiCopies.GetValueOrDefault(block);

    void IPrintDecorator.DecorateInst(PrintContext ctx, Instruction inst)
    {
        if (inst.HasResult && GetRegister(inst) is { } reg) {
            ctx.Print(" @" + reg.Index, PrintToner.Comment);
        }
        if (inst.Next == null && GetPhiCopies(inst.Block) is { } copies) {
            ctx.PrintLine();
            ctx.Print($"@({copies.Select(e => e.Dest):, $}) = ({copies.Select(e => e.Value):, $})");
        }
    }
}