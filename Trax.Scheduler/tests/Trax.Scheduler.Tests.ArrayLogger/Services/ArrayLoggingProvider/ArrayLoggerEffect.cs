using Microsoft.Extensions.Logging;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Log.DTOs;

namespace Trax.Effect.Tests.ArrayLogger.Services.ArrayLoggingProvider;

public class ArrayLoggerEffect(string categoryName) : ILogger, IDisposable
{
    private readonly object _lock = new();
    private bool _disposed = false;

    public List<Log> Logs { get; } = [];

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        if (_disposed)
            return;

        var message = formatter(state, exception);

        var log = Effect.Models.Log.Log.Create(
            new CreateLog
            {
                Level = logLevel,
                Message = message,
                CategoryName = categoryName,
                Exception = exception,
                EventId = eventId.Id,
            }
        );

        lock (_lock)
        {
            if (!_disposed)
            {
                Logs.Add(log);
            }
        }
    }

    public bool IsEnabled(LogLevel logLevel) => !_disposed;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <summary>
    /// Clears all accumulated logs to prevent memory leaks.
    /// </summary>
    public void ClearLogs()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            if (!_disposed)
            {
                Logs.Clear();
            }
        }
    }

    /// <summary>
    /// Trims logs to keep only the most recent entries, preventing unbounded growth.
    /// </summary>
    /// <param name="maxLogs">Maximum number of logs to retain</param>
    public void TrimLogs(int maxLogs)
    {
        if (_disposed || maxLogs <= 0)
            return;

        lock (_lock)
        {
            if (!_disposed && Logs.Count > maxLogs)
            {
                // Keep only the most recent logs
                var logsToKeep = Logs.Skip(Logs.Count - maxLogs).ToList();
                Logs.Clear();
                Logs.AddRange(logsToKeep);
            }
        }
    }

    /// <summary>
    /// Disposes the logger and clears all accumulated logs.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            if (_disposed)
                return;

            Logs.Clear();
            _disposed = true;
        }
    }
}
