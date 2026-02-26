using Microsoft.Extensions.Logging;

namespace Trax.Effect.Tests.ArrayLogger.Services.ArrayLoggingProvider;

public class ArrayLoggingProvider : IArrayLoggingProvider
{
    private readonly object _lock = new();
    private bool _disposed = false;

    public List<ArrayLoggerEffect> Loggers { get; } = [];

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
