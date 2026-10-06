using Microsoft.Extensions.Logging;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Log.DTOs;

namespace Trax.Mediator.Tests.ArrayLogger.Services.ArrayLoggingProvider;

/// <summary>
/// An in-memory <see cref="ILogger"/> for tests: every entry it receives is kept in
/// <see cref="Logs"/> as a Trax <see cref="Log"/> record, so a test can assert on what a train or
/// junction logged. Created by <see cref="ArrayLoggingProvider"/>, one per logger category.
/// </summary>
/// <param name="categoryName">The logger category, copied onto every <see cref="Log"/> it records (as its <c>Category</c>, truncated to 500 characters).</param>
public class ArrayLoggerEffect(string categoryName) : ILogger, IDisposable
{
    private readonly object _lock = new();
    private bool _disposed = false;

    /// <summary>
    /// The entries recorded so far, oldest first, at every <see cref="LogLevel"/> (no level is
    /// filtered out). Writes take a lock but reads through this list do not, so read it once the
    /// code under test has finished logging. Emptied by <see cref="ClearLogs"/> and
    /// <see cref="Dispose"/>.
    /// </summary>
    public List<Log> Logs { get; } = [];

    /// <summary>
    /// Formats the entry with <paramref name="formatter"/> and appends it to <see cref="Logs"/> as a
    /// <see cref="Log"/> carrying the level, message, category, exception and event id. Does nothing
    /// once the logger is disposed.
    /// </summary>
    /// <typeparam name="TState">The type of the entry's state.</typeparam>
    /// <param name="logLevel">The entry's level.</param>
    /// <param name="eventId">The entry's event id; only <see cref="EventId.Id"/> is kept.</param>
    /// <param name="state">The state passed to <paramref name="formatter"/>.</param>
    /// <param name="exception">The exception to record, or null.</param>
    /// <param name="formatter">Builds the message from <paramref name="state"/> and <paramref name="exception"/>.</param>
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

    /// <summary>True for every level until the logger is disposed, then false.</summary>
    /// <param name="logLevel">Ignored: no level is filtered.</param>
    public bool IsEnabled(LogLevel logLevel) => !_disposed;

    /// <summary>Scopes are not recorded: always returns null.</summary>
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
