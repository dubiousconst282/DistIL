using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DistIL;

using InterpHandlerArg = InterpolatedStringHandlerArgumentAttribute;

public interface ICompilationLogger
{
    LogLevel MinLevel { get; }

    void Log(LogLevel level, ReadOnlySpan<char> msg, Exception? exception = null);
    bool IsEnabled(LogLevel level);

    //TODO: figure out what to put on scope infos.
    LoggerScopeHandle Push(in LoggerScopeInfo info, string? msg = null);
    void Pop(in LoggerScopeHandle scope);

    sealed LoggerScopeHandle Push(in LoggerScopeInfo info, [InterpHandlerArg("", "info")] InterpHandler<_GenLevel._Inline> msg)
        => Push(in info, msg.ToOptString());

    sealed void Log(LogLevel level, [InterpHandlerArg("", "level")] InterpHandler<_GenLevel._Inline> msg)
        => msg.Write(this, level);
    
    sealed void Trace([InterpHandlerArg("")] InterpHandler<_GenLevel.Trace> msg) => msg.Write(this);
    sealed void Debug([InterpHandlerArg("")] InterpHandler<_GenLevel.Debug> msg) => msg.Write(this);
    sealed void Info([InterpHandlerArg("")] InterpHandler<_GenLevel.Info> msg) => msg.Write(this);
    sealed void Warn([InterpHandlerArg("")] InterpHandler<_GenLevel.Warn> msg) => msg.Write(this);
    sealed void Error([InterpHandlerArg("")] InterpHandler<_GenLevel.Error> msg) => msg.Write(this);

    sealed void Trace(ReadOnlySpan<char> msg) => Log(LogLevel.Trace, msg);
    sealed void Debug(ReadOnlySpan<char> msg) => Log(LogLevel.Debug, msg);
    sealed void Info(ReadOnlySpan<char> msg)  => Log(LogLevel.Info, msg);
    sealed void Warn(ReadOnlySpan<char> msg)  => Log(LogLevel.Warn, msg);
    sealed void Error(ReadOnlySpan<char> msg, Exception? exception = null) => Log(LogLevel.Error, msg, exception);

    [InterpolatedStringHandler, EditorBrowsable(EditorBrowsableState.Never)]
    public struct InterpHandler<TLevel>
        where TLevel : struct, _GenLevel
    {
        readonly StringBuilder? _sb;

        public InterpHandler(
            int literalLength, int formattedCount,
            ICompilationLogger logger, LogLevel level,
            out bool shouldAppend)
        {
            shouldAppend = logger.IsEnabled(level);

            if (shouldAppend) {
                _sb = new StringBuilder(literalLength + formattedCount * 16);
            }
        }

        public InterpHandler(
            int literalLength, int formattedCount,
            ICompilationLogger logger,
            out bool shouldAppend)
        : this(literalLength, formattedCount, logger, default(TLevel).Value, out shouldAppend) { }

        public InterpHandler(
            int literalLength, int formattedCount,
            ICompilationLogger logger, in LoggerScopeInfo scope,
            out bool shouldAppend)
        : this(literalLength, formattedCount, logger, LogLevel.Info, out shouldAppend) { }

        public void AppendLiteral(string value) => _sb!.Append(value);
        public void AppendFormatted(Value value) => _sb!.Append(value.ToString());
        public void AppendFormatted<T>(T value) => _sb!.Append(value);
        
        public void AppendFormatted<T>(T value, string? format = null) where T : IFormattable
        {
            if (value is not null) {
                string text = value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
                _sb!.Append(text);
            }
        }

        internal void Write(ICompilationLogger logger, LogLevel level)
        {
            if (_sb != null) {
                logger.Log(level, _sb.ToString(), null);
            }
        }
        internal void Write(ICompilationLogger logger) => Write(logger, default(TLevel).Value);

        public string? ToOptString() => _sb?.ToString();
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface _GenLevel
    {
        public LogLevel Value { get; }

        public struct _Inline : _GenLevel { public LogLevel Value => throw null!; }

        public struct Trace : _GenLevel { public LogLevel Value => LogLevel.Trace; }
        public struct Debug : _GenLevel { public LogLevel Value => LogLevel.Debug; }
        public struct Info  : _GenLevel { public LogLevel Value => LogLevel.Info; }
        public struct Warn  : _GenLevel { public LogLevel Value => LogLevel.Warn; }
        public struct Error : _GenLevel { public LogLevel Value => LogLevel.Error; }
        public struct Fatal : _GenLevel { public LogLevel Value => LogLevel.Fatal; }
    }
}
public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warn,
    Error,
    Fatal
}
public readonly struct LoggerScopeHandle : IDisposable
{
    public ICompilationLogger Logger { get; }
    public int SyncId { get; }

    public LoggerScopeHandle(ICompilationLogger logger, int syncId)
    {
        Logger = logger;
        SyncId = syncId;
    }
    public void Dispose() => Logger.Pop(this);
}
public readonly struct LoggerScopeInfo
{
    public string Name { get; }
    /// <summary> Specifies the minimum level for messages logged inside this scope. </summary>
    public LogLevel MinLevel { get; }

    public LoggerScopeInfo(string name, LogLevel minLevel = LogLevel.Trace)
    {
        Name = name;
        MinLevel = minLevel;
    }
}