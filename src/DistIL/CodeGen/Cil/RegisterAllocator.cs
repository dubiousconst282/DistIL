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

    readonly Dictionary<(TypeDesc Type, int Color), Variable> _registers = new();
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
                    //Note that we can't coalesce values of different object types,
                    //because that could lead to bad behavior for e.g. interface resolution. 
                    if (value is not Instruction valI || phi.ResultType != valI.ResultType || !_interfs.TryMerge(phi, valI)) {
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

    private Variable PickRegister(TypeDesc type, int color)
    {
        return _registers.GetOrAddRef((type, color))
            ??= new(type, name: "reg" + _registers.Count);
    }

    /// <summary> Returns the register assigned to `def`. </summary>
    public Variable GetRegister(Instruction def)
        => _interfs.GetNode(def)?.Register 
            ?? PickRegister(def.ResultType, -1); //defs with no nodes don't interfere with anything, just give them a dummy register

    /// <summary> Returns a list of phi-associated parallel copies that must execute at the end of `block`. </summary>
    public IReadOnlyList<(PhiInst Dest, Value Value)>? GetPhiCopies(BasicBlock block)
        => _phiCopies.GetValueOrDefault(block);

    void IPrintDecorator.DecorateInst(PrintContext ctx, Instruction inst)
    {
        if (inst.HasResult && GetRegister(inst) is { } reg) {
            ctx.Print(" @" + reg.Name, PrintToner.Comment);
        }
        if (inst.Next == null && GetPhiCopies(inst.Block) is { } copies) {
            ctx.PrintLine();
            ctx.Print($"@({copies.Select(e => e.Dest):, $}) = ({copies.Select(e => e.Value):, $})");
        }
    }
}