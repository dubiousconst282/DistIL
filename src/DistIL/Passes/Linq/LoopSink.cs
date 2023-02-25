namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

//Represents a sink for a query which is immediately consumed by a loop.
//
//This currently doesn't use the loop analysis but instead relies on a surrounding
//try-finally region to detect the loop.
//
//  RegionEntry:
//    try finally(RegionHandler)
//    goto Header
//  Header:
//    goto enumer.MoveNext() ? Body : Exit  //only two instrs
//  Body:
//    T item = enumer.get_Current()  //first instr
//    ...
//  Latch:
//    goto Header
//  Exit:
//    leave RegionSucc
//
//If the loop header is the only successor of a guard block, all other blocks
//must be dominated by it. Then we can safely (mostly) isolate and rewire the body
//to the newly generated expanded loop.
internal class LoopSink : LinqSink
{
    BasicBlock _regionEntry, _header, _exit, _body, _latch;
    CallInst _getCurrent, _moveNext;
    CallInst? _dispose;
    CompareInst? _nullCheck;

#pragma warning disable CS8618 //uninitialized non-nullable members
    private LoopSink(CallInst getEnumerCall)
        : base(getEnumerCall) { }
#pragma warning restore CS8618

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        //Once EmitBody() returns, the current builder's block will have its branch replaced to the latch.
        builder.SetBranch(_body);
        builder.SetPosition(_latch);

        _getCurrent.ReplaceWith(currItem);
    }
    public void RewireLoop(LoopBuilder loop)
    {
        _latch.Last.ReplaceOperand(_header, loop.Header.Block);

        _regionEntry.Last.ReplaceOperand(_header, loop.EntryBlock);
        loop.Exit.SetBranch(_exit);

        _header.Remove(); //the old header should now be unreachable and can be removed
    }
    public override void DeleteSubject()
    {
        _nullCheck?.ReplaceWith(ConstInt.CreateI(0));
        _dispose?.Remove();
        base.DeleteSubject();
    }

    public static LoopSink? TryCreate(CallInst getEnumerCall)
    {
        var sink = new LoopSink(getEnumerCall);
        return sink.MatchLoop() ? sink : null;
    }

    private bool MatchLoop()
    {
        //Uses: itr.MoveNext(), itr.get_Current(), [itr != null; itr.Dispose()]
        if (SubjectCall.NumUses is not (2 or 4)) {
            return false;
        }
        var calls = new Dictionary<string, CallInst>(4);

        //Match uses for GetEnumerator() call
        foreach (var user in SubjectCall.Users()) {
            if (user is CallInst call) {
                if (!calls.TryAdd(call.Method.Name, call)) {
                    return false;
                }
            }
            else if (user is CompareInst { Right: ConstNull } cmp && _nullCheck == null) {
                _nullCheck = cmp;
            }
            else {
                return false;
            }
        }

        return
            calls.TryGetValue("MoveNext", out _moveNext!) &&
            calls.TryGetValue("get_Current", out _getCurrent!) &&
            calls.TryGetValue("Dispose", out _dispose) &&
            MatchLoopBlocks();
    }
    private bool MatchLoopBlocks()
    {
        _header = _moveNext.Block;
        int numLatches = 0;

        foreach (var pred in _header.Preds) {
            if (pred.First is GuardInst { Kind: GuardKind.Finally }) {
                if (_regionEntry != null) {
                    return false;
                }
                _regionEntry = pred;
            } else if (pred.Last is BranchInst { IsJump: true }) {
                _latch = pred;
                numLatches++;
            } else {
                numLatches = 1000; //conditional/special backedge; must be merged
            }
        }
        return _regionEntry != null && (numLatches == 1 || (numLatches >= 2 && MergeBackedges())) &&
               //Match `Header: goto enumer.MoveNext() ? Body : Latch`
               _header.First == _moveNext && _header.Last is BranchInst br && br.Cond == _moveNext &&
               //Match `Body: T curr = enumer.get_Current()`
               (_body = br.Then).First == _getCurrent &&
               //Match `Exit: leave RegionSucc`
               (_exit = br.Else!).First is LeaveInst;
    }

    private bool MergeBackedges()
    {
        //Try reuse some block with a single jump, otherwise create a new one.
        _latch =
            _header.Preds.FirstOrDefault(b => b.First is BranchInst { IsJump: true }) ??
            _header.Method.CreateBlock(insertAfter: _header.Preds.First()); //EmitBody() will set the branch

        foreach (var pred in _header.Preds) {
            if (pred != _regionEntry && pred != _latch) {
                pred.Last.ReplaceOperand(_header, _latch);
            }
        }
        return true;
    }
}