namespace DistIL.IR.Utils;

using System.IO;
using System.Text.RegularExpressions;

public class IRPrinter
{
    public static void ExportDot(MethodBody method, string filename)
    {
        using var tw = new StreamWriter(filename);
        ExportDot(method, tw);
    }
    /// <summary> Renders the method's CFG into a dot to the specified TextWriter. </summary>
    public static void ExportDot(MethodBody method, TextWriter tw)
    {
        var pc = new GraphvizPrintContext(tw, method.GetSymbolTable());

        tw.WriteLine("digraph {");
        tw.WriteLine("  node[shape=plaintext fontname=consolas fontsize=12 fontcolor=\"#D4D4D4\"]");
        tw.WriteLine("  edge[fontname=consolas fontsize=10]");
        tw.WriteLine("  bgcolor=\"#303031\"");

        foreach (var block in method) {
            tw.Write($"  {block}[label=<\n");
            tw.Write("    <table border='0' cellborder='1' cellspacing='0' cellpadding='4' bgcolor='#1E1E1E'>\n");
            tw.Write("      <tr><td colspan='2' balign='left'>\n");

            tw.Write($"{block}:\n");
            int i = 0;

            pc.Push();
            foreach (var inst in block) {
                if (i++ != 0) pc.PrintLine();
                inst.Print(pc);
            }
            pc.Pop();

            tw.Write("</td></tr>\n");
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
            tw.Write("\n");
        }
        tw.Write("}\n");
    }

    public static void ExportPlain(MethodBody method, string filename)
    {
        using var tw = new StreamWriter(filename);
        ExportPlain(method, tw);
    }
    public static void ExportPlain(MethodBody method, TextWriter tw)
    {
        var pc = new PrintContext(tw, method.GetSymbolTable());

        foreach (var block in method) {
            pc.Print($"{block}:");
            if (block.Preds.Count > 0) {
                pc.Print($" //preds: {string.Join(" ", block.Preds)}");
            }
            pc.Push();

            int i = 0;
            foreach (var inst in block) {
                if (i++ > 0) pc.PrintLine();
                inst.Print(pc);
            }
            pc.Pop();
        }
    }

    class GraphvizPrintContext : PrintContext
    {
        public GraphvizPrintContext(TextWriter output, SymbolTable symTable) 
            : base(output, symTable)
        {
        }

        public override void Print(string str, PrintToner toner = PrintToner.Default)
        {
            str = Regex.Replace(str, @"[&<>\n]| [^a-zA-Z_]", m => {
                return m.ValueSpan switch {
                    "&" => "&amp;",
                    "<" => "&lt;",
                    ">" => "&gt;",
                    "\n" => "<br/>\n",
                    [' ', char c] => $"<b> </b>{c}", //graphviz is buggy and won't render spaces between font tags correctly
                    _ => m.Value
                };
            });

            string? color = null;
            if (toner != PrintToner.Default && _colors.TryGetValue(toner, out color)) {
                Output.Write($"<font color=\"{color}\">");
            }
            Output.Write(str);
            
            if (color != null) Output.Write("</font>");
        }

        static readonly Dictionary<PrintToner, string> _colors = new() {
            //VSCode Dark+ palette
            { PrintToner.Keyword,   "#569cd6" },
            { PrintToner.Comment,   "#6a9955" },
            { PrintToner.VarName,   "#9cdcfe" },
            { PrintToner.InstName,  "#c586c0" },
            { PrintToner.TypeName,  "#4ec9b0" },
            { PrintToner.MemberName,"#4fc1ff" },
            { PrintToner.MethodName,"#dcdcaa" },
            { PrintToner.String,    "#ce9178" },
            { PrintToner.Number,    "#b5cea8" },
        };
    }

    public static void ExportForest(MethodBody method, string filename)
    {
        using var tw = new StreamWriter(filename);
        ExportForest(method, tw);
    }
    public static void ExportForest(MethodBody method, TextWriter tw)
    {
        var pc = new PrintContext(tw, method.GetSymbolTable());
        var forest = new Forestifier(method);

        foreach (var block in method) {
            tw.Write($"{block}:");
            pc.Push();

            int i = 0;
            foreach (var inst in block) {
                if (!forest.IsRootedTree(inst)) continue;

                if (i++ > 0) pc.PrintLine();

                if (inst.HasResult) {
                    inst.ResultType.Print(pc);
                    pc.Print(" ");
                    inst.PrintAsOperand(pc);
                    pc.Print(" = ");
                }
                PrintExpr(inst, 0);
            }
            pc.Pop();
        }

        void PrintExpr(Value val, int depth)
        {
            if (val is Instruction inst) {
                PrintExpr_I(inst, depth);
            } else {
                val.PrintAsOperand(pc);
            }
        }
        void PrintExpr_I(Instruction inst, int depth)
        {
            if (depth > 0 && forest.IsRootedTree(inst)) {
                inst.PrintAsOperand(pc);
                return;
            }
            if (depth > 0) pc.Print("(");
            pc.Print(inst.InstName);
            int operIdx = 0;
            foreach (var oper in inst.Operands) {
                pc.Print(operIdx++ > 0 ? ", " : " ");
                PrintExpr(oper, depth + 1);
            }
            if (depth > 0) pc.Print(")");
        }
    }
}