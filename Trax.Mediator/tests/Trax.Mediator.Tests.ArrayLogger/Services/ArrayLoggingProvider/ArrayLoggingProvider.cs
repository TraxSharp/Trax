using Microsoft.Extensions.Logging;

namespace Trax.Mediator.Tests.ArrayLogger.Services.ArrayLoggingProvider;

/// <summary>
/// An <see cref="ILoggerProvider"/> for tests that hands out an <see cref="ArrayLoggerEffect"/> per
/// <see cref="CreateLogger"/> call and keeps every one of them in <see cref="Loggers"/>, so a test can
/// read what was logged under any category. Register it with <c>AddProvider</c> in a test host; it is
/// not meant for production, since entries are only dropped when you clear, trim or dispose.
/// </summary>
public class ArrayLoggingProvider : IArrayLoggingProvider
{
    private readonly object _lock = new();
    private bool _disposed = false;

    /// <inheritdoc/>
    public List<ArrayLoggerEffect> Loggers { get; } = [];

    /// <summary>
    /// Disposes every logger (which empties its <see cref="ArrayLoggerEffect.Logs"/>) and empties
    /// <see cref="Loggers"/>. Later calls do nothing; <see cref="CreateLogger"/> throws afterwards.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            if (_disposed)
                return;

            // Dispose all loggers and clear their logs
            foreach (var logger in Loggers)
            {
                logger.Dispose();
            }

            // Clear the loggers list to release references
            Loggers.Clear();
            _disposed = true;
        }
    }

    /// <summary>
    /// Creates a new <see cref="ArrayLoggerEffect"/> for <paramref name="categoryName"/> and adds it
    /// to <see cref="Loggers"/>. Every call creates a new logger, even for a category seen before;
    /// the logging framework normally caches one per category itself.
    /// </summary>
    /// <param name="categoryName">The logger category.</param>
    /// <exception cref="ObjectDisposedException">The provider has been disposed.</exception>
    public ILogger CreateLogger(string categoryName)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ArrayLoggingProvider));

        lock (_lock)
        {
            var logger = new ArrayLoggerEffect(categoryName);
            Loggers.Add(logger);
            return logger;
        }
    }

    /// <summary>
    /// Clears all loggers and their accumulated logs to prevent memory leaks.
    /// Call this periodically in long-running applications.
    /// </summary>
    public void ClearAllLogs()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            foreach (var logger in Loggers)
            {
                logger.ClearLogs();
            }
        }
    }

    /// <summary>
    /// Removes loggers that exceed the specified log count to prevent unbounded growth.
    /// </summary>
    /// <param name="maxLogsPerLogger">Maximum number of logs to keep per logger</param>
    public void TrimLoggers(int maxLogsPerLogger = 1000)
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            foreach (var logger in Loggers)
            {
                logger.TrimLogs(maxLogsPerLogger);
            }
        }
    }
}
