using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Flagbit.Api.Tests;

internal sealed class CapturedLogs : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturedLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturedLogger(CapturedLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            owner.Entries.Enqueue((logLevel, $"{category}: {formatter(state, exception)} {exception}"));
        }
    }
}
