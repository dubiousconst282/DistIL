namespace DistIL.IR.Utils;

using System.Text.RegularExpressions;

using DistIL.Analysis;

using MethodAttribs = System.Reflection.MethodAttributes;

public class IRPrinter
{
    /// <summary> Renders the method's CFG into a graphviz file. </summary>
    /// <remarks>
    /// Graphviz files can be rendered in several ways, one of them is through this VSCode extension: 
    /// https://marketplace.visualstudio.com/items?itemName=tintinweb.graphviz-interactive-preview
    /// </remarks>
    public static void ExportDot(MethodBody method, string filename, IReadOnlyList<IPrintDecorator>? decorators = null)
    {
        using var tw = new StreamWriter(filename);
        ExportDot(method, tw, decorators);
    }
    public static void ExportDot(MethodBody method, TextWriter tw, IReadOnlyList<IPrintDecorator>? decorators = null)
    {
        var pc = new GraphvizPrintContext(tw, method.GetSymbolTable());
        bool hasGuards = false;

        tw.WriteLine("digraph {");
        tw.WriteLine("  node[shape=plaintext fontname=consolas fontsize=12 fontcolor=\"#D4D4D4\"]");
        tw.WriteLine("  edge[fontname=consolas fontsize=10 fontcolor=\"#D4D4D4\"]");
        tw.WriteLine("  bgcolor=\"#303031\"");

        foreach (var block in method) {
            tw.Write($"  {block}[label=<\n");
            tw.Write("    <table border='0' cellborder='1' cellspacing='0' cellpadding='4' bgcolor='#1E1E1E'>\n");
            tw.Write("      <tr><td colspan='2' balign='left'>\n");

            tw.Write($"{block}:\n");
            Decorate(d => d.DecorateLabel(pc, block));

            int i = 0;
            pc.Push();
            foreach (var inst in block) {
                if (i++ != 0) pc.PrintLine();
                inst.Print(pc);
                Decorate(d => d.DecorateInst(pc, inst));
            }
            pc.Pop();

            tw.Write("</td></tr>\n");
            tw.Write("    </table>\n");
            tw.Write("  >]\n");

            foreach (var succ in block.Succs) {
                string port = "s";
                var style = new GraphvizEdgeStyle();

                if (block.Last is BranchInst { IsConditional: true } br) {
                    port = succ == br.Then ? "sw" : "se";
                } else if (block.Last is LeaveInst) {
                    style.Color = "red";
                }
                if (block.Guards().Any(g => g.HandlerBlock == succ || g.FilterBlock == succ)) {
                    port = "e";
                    style.Dashed = true;
                    style.Color = "gray";
                }
                Decorate(d => d.DecorateEdge(block, succ, ref style));
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

        void Decorate(Action<IPrintDecorator> fn)
        {
            if (decorators != null) {
                foreach (var decor in decorators) {
                    fn(decor);
                }
            }
        }
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

        var def = method.Definition;

        pc.Print($"{PrintToner.Keyword}{StringifyMethodAttribs(def.Attribs)} {def.DeclaringType.Name}::{def.Name}");
        pc.PrintSequence("(", ")", method.Args.Skip(def.IsInstance ? 1 : 0), arg => pc.Print($"{arg}: {arg.ResultType}"));
        pc.Print($" -> {def.ReturnType} {{\n");

        var declaredVars = method.Instructions().OfType<VarAccessInst>().Select(a => a.Var).Distinct();

        if (declaredVars.Any()) {
            pc.Print("$Locals:\n");
            foreach (var group in declaredVars.GroupBy(v => (v.Sig.Type, v.IsPinned))) {
                pc.PrintSequence("  ", "", group, v => pc.Print(v.ToString()[1..], PrintToner.VarName));
                pc.Print($": {group.Key.Type}{(group.Key.IsPinned ? "^" : "")}\n");
            }
        }

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
        pc.Print("}");
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

    private static string StringifyMethodAttribs(MethodAttribs attribs)
    {
        var acc = attribs & MethodAttribs.MemberAccessMask;
        string str = acc switch {
            MethodAttribs.Private       => "private",
            MethodAttribs.FamANDAssem   => "private protected",
            MethodAttribs.Assembly      => "internal",
            MethodAttribs.Family        => "protected",
            MethodAttribs.FamORAssem    => "protected internal",
            MethodAttribs.Public        => "public"
        };

        if (attribs.HasFlag(MethodAttribs.Static)) {
            str += " static";
        }
        if (attribs.HasFlag(MethodAttribs.SpecialName)) {
            str += " special";
        }
        if (attribs.HasFlag(MethodAttribs.Final)) {
            str += " sealed";
        }
        if (attribs.HasFlag(MethodAttribs.Abstract)) {
            str += " abstract";
        } else if (attribs.HasFlag(MethodAttribs.Virtual)) {
            str += " virtual";
        }
        return str;
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
            int untrimmedLen = str.Length;
            str = str.TrimStart(' ');

            if (str.Length != untrimmedLen) {
                string ws = new(' ', untrimmedLen - str.Length);
                Output.Write("<b>" + ws + "</b>");
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

/// <summary> Prints additional information on specific locations of the textual form IR. </summary>
public interface IPrintDecorator
{
    void DecorateLabel(PrintContext ctx, BasicBlock block) { }
    void DecorateInst(PrintContext ctx, Instruction inst) { }

    /// <remarks> Graphviz only. </remarks>
    void DecorateEdge(BasicBlock block, BasicBlock succ, ref GraphvizEdgeStyle style) { }
}
public struct GraphvizEdgeStyle
{
    public string? Color;
    public string? OutLabel, InLabel;
    public bool Dashed;

    public override string ToString()
    {
        var sb = new StringBuilder("[");
        Add("color=", Color);
        Add("style=", Dashed ? "dashed" : null);
        Add("taillabel=", OutLabel);
        Add("headlabel=", InLabel);
        return sb.Length == 1 ? "" : sb.Append(']').ToString();

        void Add(string key, string? val)
        {
            if (val != null) {
                sb.Append(key).Append('"').Append(val.Replace("\n", "\\\n")).Append('"');
            }
        }
    }
}