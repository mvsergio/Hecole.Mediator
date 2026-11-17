using Microsoft.Extensions.Logging;

namespace Hecole.Mediator.Tests.Fakes;

/// <summary>One log call: level, event id, formatted message, structured values and exception.</summary>
public sealed record LogEntry(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyList<KeyValuePair<string, object?>> Values,
    Exception? Exception);

/// <summary>Shared store of log entries (singleton in the container, or created by hand).</summary>
public sealed class LogSink
{
    private readonly List<LogEntry> _entries = [];
    private readonly Lock _gate = new();

    /// <summary>Levels below this are reported as disabled.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

    public IReadOnlyList<LogEntry> Entries
    {
        get { lock (_gate) return [.. _entries]; }
    }

    public int IsEnabledCalls { get; private set; }

    public bool IsEnabled(LogLevel level)
    {
        IsEnabledCalls++;
        return level >= MinimumLevel;
    }

    public void Add(LogEntry entry)
    {
        lock (_gate) _entries.Add(entry);
    }
}

/// <summary>Logger without a mock library: records every call in a <see cref="LogSink"/>.</summary>
public sealed class FakeLogger<T>(LogSink sink) : ILogger<T>
{
    public LogSink Sink { get; } = sink;

    public FakeLogger() : this(new LogSink())
    {
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => Sink.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
        Sink.Add(new LogEntry(typeof(T).FullName!, logLevel, eventId, formatter(state, exception), [.. values], exception));
    }
}
