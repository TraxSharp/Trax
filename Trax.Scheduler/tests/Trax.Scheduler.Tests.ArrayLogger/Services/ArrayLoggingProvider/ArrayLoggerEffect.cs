using Microsoft.Extensions.Logging;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Log.DTOs;

namespace Trax.Scheduler.Tests.ArrayLogger.Services.ArrayLoggingProvider;

/// <summary>
/// An <see cref="ILogger"/> for one category that keeps every entry it receives in
/// <see cref="Logs"/>, so a test can assert on what was logged. Created by
/// <see cref="ArrayLoggingProvider.CreateLogger"/>; despite the name it is a logger, not a Trax effect.
/// </summary>
/// <param name="categoryName">The logger category, copied onto every captured entry.</param>
public class ArrayLoggerEffect(string categoryName) : ILogger, IDisposable
{
    private readonly object _lock = new();
    private bool _disposed = false;

    /// <summary>
    /// Every entry captured so far, oldest first. Writes are locked, but the list itself is not
    /// thread-safe: read it once the code under test has stopped logging. Emptied on dispose.
    /// </summary>
    public List<Log> Logs { get; } = [];

    /// <summary>
    /// Formats the entry and appends it to <see cref="Logs"/> with its level, message, category,
    /// exception and event id. Every level is captured; nothing is filtered. Ignored after dispose.
    /// </summary>
    /// <typeparam name="TState">The type of the state object.</typeparam>
    /// <param name="logLevel">Stored on the entry.</param>
    /// <param name="eventId">Its <c>Id</c> is stored on the entry.</param>
    /// <param name="state">Passed to <paramref name="formatter"/>.</param>
    /// <param name="exception">Stored on the entry, and passed to <paramref name="formatter"/>.</param>
    /// <param name="formatter">Produces the stored message.</param>
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

    /// <summary>Returns <see langword="true"/> for every level until the logger is disposed.</summary>
    /// <param name="logLevel">Ignored.</param>
    public bool IsEnabled(LogLevel logLevel) => !_disposed;

    /// <summary>Scopes are not supported: returns <see langword="null"/> and records nothing.</summary>
    /// <typeparam name="TState">The type of the scope state.</typeparam>
    /// <param name="state">Ignored.</param>
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
