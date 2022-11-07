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

    public void Print(Value value) => value.Print(this);
    public void PrintAsOperand(Value value) => value.PrintAsOperand(this);

    public virtual void Print(string str, PrintToner toner = default) => Output.Write(str);

    public void Print([InterpolatedStringHandlerArgument("")] InterpolationHandler handler) { }
    
    public void PrintSequence<T>(string prefix, string postfix, IReadOnlyList<T> elems, Action<T> printElem)
    {
        Print(prefix);
        for (int i = 0; i < elems.Count; i++) {
            if (i > 0) Print(", ");
            printElem(elems[i]);
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

        public void AppendFormatted(Value value)
        {
            if (value is Instruction) {
                value.PrintAsOperand(_ctx);
            } else {
                value.Print(_ctx);
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
public enum PrintToner
{
    Default,
    Keyword,
    Comment,
    VarName,
    InstName,
    TypeName,
    MemberName,
    MethodName,
    String,
    Number,
}