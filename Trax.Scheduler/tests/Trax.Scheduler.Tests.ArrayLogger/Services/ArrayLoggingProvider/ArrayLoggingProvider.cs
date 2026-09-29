using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Tests.ArrayLogger.Services.ArrayLoggingProvider;

/// <summary>
/// An <see cref="ILoggerProvider"/> that hands out <see cref="ArrayLoggerEffect"/> instances and
/// keeps them, so a test can read every captured entry through <see cref="Loggers"/>. Register it
/// with <c>builder.AddProvider(...)</c> in a test host; it is not meant for production logging,
/// because it keeps every entry in memory until cleared or disposed.
/// </summary>
public class ArrayLoggingProvider : IArrayLoggingProvider
{
    private readonly object _lock = new();
    private bool _disposed = false;

    /// <summary>
    /// Every logger created so far, in creation order; one per <see cref="CreateLogger"/> call, so
    /// the same category can appear more than once. Emptied on dispose.
    /// </summary>
    public List<ArrayLoggerEffect> Loggers { get; } = [];

    /// <summary>
    /// Disposes every logger, which empties its <see cref="ArrayLoggerEffect.Logs"/>, and clears
    /// <see cref="Loggers"/>. Read what you need before disposing. Safe to call more than once.
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
    /// to <see cref="Loggers"/>. Loggers are not cached by category.
    /// </summary>
    /// <param name="categoryName">The category stamped on the logger's entries.</param>
    /// <returns>The new logger.</returns>
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
