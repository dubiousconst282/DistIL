namespace DistIL.IR.Utils;

using System.IO;

public class IRPrinter
{
    public static void ExportDot(Method method, string filename)
    {
        using var tw = new StreamWriter(filename);
        ExportDot(method, tw);
    }
    /// <summary> Renders the method's CFG into a dot to the specified TextWriter. </summary>
    public static void ExportDot(Method method, TextWriter tw)
    {
        var instSb = new StringBuilder();
        var slotTracker = method.GetSlotTracker();

        //var domTree = new DominatorTree(method);
        //var postDomTree = new DominatorTree(method, true);

        tw.WriteLine("digraph {");
        tw.WriteLine("  node[shape=plaintext fontname=consolas fontsize=12]");
        tw.WriteLine("  edge[fontname=consolas fontsize=10]");

        foreach (var block in method) {
            tw.Write($"  {block}[label=<\n");
            tw.Write("    <table border='0' cellborder='1' cellspacing='0' cellpadding='4'>\n");
            tw.Write("      <tr><td colspan='2' balign='left'>\n");

            tw.Write($"{block}: {(block == method.EntryBlock ? "//entry" : "")} <br/>\n");
            int i = 0;
            foreach (var inst in block) {
                if (i++ != 0) tw.Write(" <br/>\n");
                tw.Write("  ");
                inst.Print(instSb, slotTracker);
                instSb.Replace("&", "&amp;");
                instSb.Replace("<", "&lt;");
                instSb.Replace(">", "&gt;");
                instSb.Replace("\n", "<br/>\n  ");
                tw.Write(instSb);
                instSb.Clear();
            }
            tw.Write("\n      </td></tr>\n");
            if (block.Last is BranchInst { IsConditional: true }) {
                tw.Write("      <tr> <td port='T'>T</td> <td port='F'>F</td> </tr>\n");
            }

            tw.Write("    </table>\n");
            tw.Write("  >]\n");

            foreach (var succ in block.Succs) {
                string port = "s";
                string style = "";
                if (block.Last is BranchInst { IsConditional: true } br) {
                    port = succ == br.Then ? "T" : "F";
                }
                if (block.Last is LeaveInst) {
                    style = "[color=red]";
                }
                foreach (var inst in block) {
                    if (inst is not GuardInst guard) break;
                    if (guard.HandlerBlock == succ || guard.FilterBlock == succ) {
                        (style, port) = ("[style=dashed color=gray]", "e");
                    }
                }
                tw.Write($"  {block}:{port} -> {succ}{style}\n");
            }

            //var dom = domTree.IDom(block);
            //var postDom = postDomTree.IDom(block);

            //if (dom != null) {
            //    tw.Write($"  {block}:nw -> {dom}[style=dashed color=red]\n");
            //}
            //if (postDom != null) {
            //    tw.Write($"  {block}:nw -> {postDom}[style=dashed color=green]\n");
            //}
            //foreach (var frontier in block.DomFrontier) {
            //    tw.Write($"  {block}:se -> {frontier}[style=dashed color=blue]\n");
            //}
            tw.Write("\n");
        }
        tw.Write("}\n");
    }

    public static void ExportPlain(Method method, string filename)
    {
        using var tw = new StreamWriter(filename);
        ExportPlain(method, tw);
    }
    public static void ExportPlain(Method method, TextWriter tw)
    {
        var tmpSb = new StringBuilder();
        var slotTracker = method.GetSlotTracker();

        foreach (var block in method) {
            tw.Write($"{block}:");
            if (block.Preds.Count > 0) {
                tmpSb.Append("preds: ").AppendJoin(" ", block.Preds);
            } else if (block == method.EntryBlock) {
                tmpSb.Append("entry");
            }

            foreach (var use in block.Uses) {
                if (use.Inst is GuardInst tryInst) {
                    tmpSb.Append(block == tryInst.FilterBlock ? " filter <- " : $" {tryInst.Kind.ToString().ToLower()} <- ");
                    use.Inst.PrintAsOperand(tmpSb, slotTracker);
                }
            }

            if (tmpSb.Length > 0) {
                tw.Write(" //");
                if (tmpSb[0] == ' ') {
                    tmpSb.Remove(0, 1);
                }
                tw.Write(tmpSb);
                tmpSb.Clear();
            }
            tw.WriteLine();

            foreach (var inst in block) {
                tw.Write("  ");
                inst.Print(tmpSb, slotTracker);
                tmpSb.Replace("\n", "\n  ");

                tw.WriteLine(tmpSb);
                tmpSb.Clear();
            }
        }
    }
}