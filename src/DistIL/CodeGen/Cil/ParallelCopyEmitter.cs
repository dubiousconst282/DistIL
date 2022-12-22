namespace DistIL.CodeGen.Cil;

//This uses the algorithm from "Revisiting Out-of-SSA Translation for Correctness, Code Quality, and Efficiency"
//by Boissinot et al. (https://hal.inria.fr/inria-00349925v1/document)
//
//Note that the pseudo-code given in the paper has a typo, it should be `pendingDst != Loc(Pred(pendingDst))`.
//It also breaks if a source is used multiple times; A dest should only be added to `pending` if its Loc() is null.
//Other info: https://github.com/pfalcon/parcopy
public class ParallelCopyEmitter
{
    List<Variable> _dests = new();
    ArrayStack<Variable> _ready = new();
    ArrayStack<Variable> _pending = new();
    Dictionary<Variable, (Variable? Pred, Variable? Loc)> _links = new();

    public int Count => _dests.Count;

    public void Add(Variable dest, Variable src)
    {
        if (dest == src) return;
        
        Loc(src) = src;
        Pred(dest) = src;

        Debug.Assert(!_dests.Contains(dest));
        _dests.Add(dest);
    }

    public void SequentializeAndClear(Action<Variable, Variable> emitCopy)
    {
        if (_dests.Count == 1) {
            var dest = _dests[0];
            emitCopy(dest, Pred(dest)!);
        } else {
            SequentializeMany(emitCopy);
            Debug.Assert(_ready.Count == 0 && _pending.Count == 0);
        }
        _links.Clear();
        _dests.Clear();
    }

    private void SequentializeMany(Action<Variable, Variable> emitCopy)
    {
        foreach (var dest in _dests) {
            if (Loc(dest) == null) {
                _ready.Push(dest); //dest is unused and can be overwritten
            } else {
                _pending.Push(dest); //dest may need a temp
            }
        }
        while (true) {
            while (_ready.TryPop(out var dest)) {
                var src = Pred(dest)!;
                var currLoc = Loc(src)!;
                emitCopy(dest, currLoc);
                Loc(src) = dest;

                if (src == currLoc && Pred(src) != null) {
                    _ready.Push(src);
                }
            }
            if (_pending.IsEmpty) break;

            var pendingDest = _pending.Pop();
            if (pendingDest != Loc(Pred(pendingDest)!)) {
                var tempSlot = new Variable(pendingDest.ResultType);
                emitCopy(tempSlot, pendingDest);

                Loc(pendingDest) = tempSlot;
                _ready.Push(pendingDest);
            }
        }
    }

    private ref Variable? Loc(Variable var) => ref _links.GetOrAddRef(var).Loc;
    private ref Variable? Pred(Variable var) => ref _links.GetOrAddRef(var).Pred;
}