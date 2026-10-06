using Microsoft.Extensions.Logging;

namespace Trax.Mediator.Tests.ArrayLogger.Services.ArrayLoggingProvider;

/// <summary>
/// A test <see cref="ILoggerProvider"/> whose loggers stay reachable after creation, so a test can
/// inspect what was logged. Implemented by <see cref="ArrayLoggingProvider"/>.
/// </summary>
public interface IArrayLoggingProvider : ILoggerProvider
{
    /// <summary>
    /// Every logger this provider has created, in creation order. Search it by
    /// each entry's <c>Category</c> or flatten the <see cref="ArrayLoggerEffect.Logs"/> lists to find
    /// an entry. Not synchronized for readers: read it after the code under test has finished.
    /// </summary>
    public List<ArrayLoggerEffect> Loggers { get; }
}
