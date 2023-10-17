namespace DistIL.IR;

using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

public class PrintContext
{
    public TextWriter Output { get; }
    public SymbolTable SymTable { get; }
    public int IndentLevel { get; set; }

    public PrintContext(TextWriter output, SymbolTable symTable)
    {
        Output = output;
        SymTable = symTable;
    }

    public static string ToString(IPrintable obj, SymbolTable? symTable = null)
    {
        var sw = new StringWriter();
        obj.Print(new PrintContext(sw, symTable ?? SymbolTable.Empty));
        return sw.ToString();
    }

    public virtual void Print(IPrintable value) => value.Print(this);
    public virtual void PrintAsOperand(IPrintable value) => value.PrintAsOperand(this);

    public virtual void Print(string str, PrintToner toner = default) => Output.Write(str);

    public void Print([InterpolatedStringHandlerArgument("")] InterpolationHandler handler) { }
    
    public void PrintSequence<T>(string prefix, string postfix, IEnumerable<T> elems, Action<T> printElem)
    {
        Print(prefix);
        int i = 0;
        foreach (var elem in elems) {
            if (i++ != 0) Print(", ");
            printElem(elem);
        }
        Print(postfix);
    }

    public void Push(string? brace = null)
    {
        if (brace != null) Print(brace);
        IndentLevel++;
        PrintLine();
    }
    public void Pop(string? brace = null)
    {
        IndentLevel--;
        PrintLine();
        if (brace != null) Print(brace);
    }
    public void PrintLine(string? text = null)
    {
        if (text != null) Print(text);
        Print("\n");
        PrintIndent();
    }

    public void PrintIndent()
    {
        for (int i = 0; i < IndentLevel; i++) {
            Print("  ");
        }
    }

    [InterpolatedStringHandler]
    public ref struct InterpolationHandler
    {
        PrintContext _ctx;
        PrintToner _nextToner = default;

        public InterpolationHandler(int literalLength, int formattedCount, PrintContext ctx)
        {
            _ctx = ctx;
        }

        public void AppendLiteral(string str) => Print(str);
        public void AppendFormatted(string str) => Print(str);
    
        public void AppendFormatted(PrintToner nextToner)
            => _nextToner = nextToner;
            
        public void AppendFormatted<T>(T value) where T : IFormattable
            => Print(value.ToString(null, CultureInfo.InvariantCulture));

        public void AppendFormatted(IPrintable value)
        {
            if (value is Instruction) {
                _ctx.PrintAsOperand(value);
            } else {
                _ctx.Print(value);
            }
            _nextToner = default;
        }

        public void AppendFormatted(IEnumerable<IPrintable> values, string format)
        {
            //Trailing whitespace is not allowed in interpolated strings,
            //so we use '$' as a backward escaping character instead.
            string separator = format.TrimEnd('$');
            int i = 0;
            foreach (var value in values) {
                if (i ++ > 0) {
                    _ctx.Print(separator, _nextToner);
                }
                if (value is Instruction) {
                    _ctx.PrintAsOperand(value);
                } else {
                    _ctx.Print(value);
                }
            }
            _nextToner = default;
        }

        private void Print(string str)
        {
            _ctx.Print(str, _nextToner);
            _nextToner = default;
        }
    }
}

public interface IPrintable
{
    void Print(PrintContext ctx);
    void PrintAsOperand(PrintContext ctx);
}

public enum PrintToner
{
    Default,
    Keyword,
    Comment,
    VarName,
    InstName,
    ClassName,
    StructName,
    MemberName,
    MethodName,
    String,
    Number,
}