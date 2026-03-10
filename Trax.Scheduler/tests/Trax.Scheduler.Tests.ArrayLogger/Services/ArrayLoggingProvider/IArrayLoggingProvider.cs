using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Tests.ArrayLogger.Services.ArrayLoggingProvider;

public interface IArrayLoggingProvider : ILoggerProvider
{
    public List<ArrayLoggerEffect> Loggers { get; }
}
