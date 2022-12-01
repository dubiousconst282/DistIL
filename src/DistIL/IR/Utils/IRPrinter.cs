namespace DistIL.IR.Utils;

using System.IO;
using System.Text.RegularExpressions;

using DistIL.Analysis;

public class IRPrinter
{
    /// <summary> Renders the method's CFG into a graphviz file. </summary>
    /// <remarks>
    /// Graphviz files can be rendered in several ways, one of them is through this VSCode extension: 
    /// https://marketplace.visualstudio.com/items?itemName=tintinweb.graphviz-interactive-preview
    /// </remarks>
    public static void ExportDot(MethodBody method, string filename)
    {
        using var tw = new StreamWriter(filename);
        ExportDot(method, tw);
    }
    public static void ExportDot(MethodBody method, TextWriter tw)
    {
        //TODO: IGraphvizDecorator to allow analyses to draw custom elements
        var pc = new GraphvizPrintContext(tw, method.GetSymbolTable());
        bool hasGuards = false;

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
            tw.Write("    </table>\n");
            tw.Write("  >]\n");

            foreach (var succ in block.Succs) {
                string port = "s", style = "";
                if (block.Last is BranchInst { IsConditional: true } br) {
                    port = succ == br.Then ? "sw" : "se";
                } else if (block.Last is LeaveInst) {
                    style = "[color=red]";
                }
                foreach (var guard in block.Guards()) {
                    if (guard.HandlerBlock == succ || guard.FilterBlock == succ) {
                        (style, port) = ("[style=dashed color=gray]", "e");
                        break;
                    }
                }
                tw.Write($"  {block}:{port} -> {succ}{style}\n");
            }
            tw.Write("\n");

            hasGuards |= block.Guards().Any();
        }

        if (hasGuards) {
            int clusterId = 0;
            var regionAnalysis = new ProtectedRegionAnalysis(method);
            PrintCluster(regionAnalysis.Root, "  ");

            void PrintCluster(ProtectedRegion region, string indent)
            {
                tw.Write($"{indent}subgraph cluster_{++clusterId} {{\n{indent}  ");
                if (region != regionAnalysis.Root) {
                    bool isHandler = region.StartBlock.Users().Any(u => u is GuardInst);
                    tw.Write($"bgcolor=\"{(isHandler ? "#0000FF08" : "#00FF000A")}\" ");
                    tw.Write($"style=\"{(isHandler ? "dashed" : "solid")}\"\n");
                } else {
                    tw.Write($"style=\"invis\"\n");
                }
                tw.Write(indent + "  ");
                foreach (var block in region.Blocks) {
                    tw.Write(block + " ");
                }
                tw.Write("\n");
                foreach (var child in region.Children) {
                    PrintCluster(child, indent + "  ");
                }
                tw.Write($"{indent}}}\n");
            }
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
        var pc = tw == Console.Out //TODO: better api for this
            ? new VTAnsiPrintContext(tw, method.GetSymbolTable()) 
            : new PrintContext(tw, method.GetSymbolTable());

        foreach (var block in method) {
            pc.Print($"{block}:");
            if (block.NumPreds > 0) {
                pc.Print($" //preds: {string.Join(" ", block.Preds.AsEnumerable())}");
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

    public static void ExportForest(MethodBody method, string filename)
    {
        using var tw = new StreamWriter(filename);
        var forest = new ForestAnalysis(method);
        var pc = new ForestPrintContext(tw, method.GetSymbolTable(), forest);

        foreach (var block in method) {
            tw.Write($"{block}:");
            pc.Push();

            int i = 0;
            foreach (var inst in block) {
                if (!forest.IsTreeRoot(inst)) continue;

                if (i++ > 0) pc.PrintLine();
                pc.Print(inst);
            }
            pc.Pop();
        }
    }

    class GraphvizPrintContext : PrintContext
    {
        public GraphvizPrintContext(TextWriter output, SymbolTable symTable)
            : base(output, symTable) { }

        public override void Print(string str, PrintToner toner = PrintToner.Default)
        {
            str = Regex.Replace(str, @"[&<>\n]", m => m.ValueSpan switch {
                "&" => "&amp;",
                "<" => "&lt;",
                ">" => "&gt;",
                "\n" => "<br/>\n",
                _ => m.Value
            });

            //Graphviz doesn't render spaces between font tags correctly,
            //this workaround seem to work most of the time.
            if (str.StartsWith(' ')) {
                Output.Write("<b> </b>");
                str = str[1..];
            }
            
            string? color = null;
            if (toner != PrintToner.Default && _colors.TryGetValue(toner, out color)) {
                Output.Write($"<font color=\"{color}\">");
            }
            Output.Write(str);
            
            if (color != null) Output.Write("</font>");
        }

        static readonly Dictionary<PrintToner, string> _colors = new() {
            //VS Dark palette
            { PrintToner.Keyword,    "#569cd6" },
            { PrintToner.Comment,    "#6a9955" },
            { PrintToner.VarName,    "#9cdcfe" },
            { PrintToner.InstName,   "#c586c0" },
            { PrintToner.ClassName,  "#4ec9b0" },
            { PrintToner.StructName, "#86C691" },
            { PrintToner.MemberName, "#4fc1ff" },
            { PrintToner.MethodName, "#dcdcaa" },
            { PrintToner.String,     "#ce9178" },
            { PrintToner.Number,     "#b5cea8" },
        };
    }
    class VTAnsiPrintContext : PrintContext
    {
        const string Esc = "\x1B[";

        public VTAnsiPrintContext(TextWriter output, SymbolTable symTable)
            : base(output, symTable) { }

        public override void Print(string str, PrintToner toner = PrintToner.Default)
        {
            string? color = null;
            if (toner != PrintToner.Default && _colors.TryGetValue(toner, out color)) {
                Output.Write(color);
            }
            Output.Write(str);

            if (color != null) Output.Write(Esc + "0m"); //reset
        }

        static readonly Dictionary<PrintToner, string> _colors = new() {
            { PrintToner.Keyword,    Esc + "94m" }, //Bright Blue
            { PrintToner.Comment,    Esc + "32m" }, //Dark Green
            { PrintToner.VarName,    Esc + "97m" }, //White
            { PrintToner.InstName,   Esc + "95m" }, //Bright Magenta
            { PrintToner.ClassName,  Esc + "36m" }, //Cyan
            { PrintToner.StructName, Esc + "96m" }, //Bright Cyan
            { PrintToner.MemberName, Esc + "37m" }, //Light Gray
            { PrintToner.MethodName, Esc + "93m" }, //Yellow
            { PrintToner.String,     Esc + "92m" }, //Green
            { PrintToner.Number,     Esc + "92m" }, //Green
        };
    }

    class ForestPrintContext : PrintContext
    {
        readonly ForestAnalysis _forest;

        public ForestPrintContext(TextWriter output, SymbolTable symTable, ForestAnalysis forest)
            : base(output, symTable)
        {
            _forest = forest;
        }

        public override void PrintAsOperand(Value value)
        {
            if (value is Instruction inst && _forest.IsLeaf(inst)) {
                Print("(");
                inst.PrintWithoutPrefix(this);
                Print(")");
            } else {
                base.PrintAsOperand(value);
            }
        }
    }
}