using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Tests.ArrayLogger.Services.ArrayLoggingProvider;

/// <summary>
/// A logger provider whose loggers keep their entries in memory for test assertions. Implemented
/// by <see cref="ArrayLoggingProvider"/>.
/// </summary>
public interface IArrayLoggingProvider : ILoggerProvider
{
    /// <summary>Every logger the provider has created, in creation order.</summary>
    public List<ArrayLoggerEffect> Loggers { get; }
}
