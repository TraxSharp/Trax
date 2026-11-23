using Microsoft.Extensions.Logging;

namespace Trax.Effect.Tests.ArrayLogger.Services.ArrayLoggingProvider;

public interface IArrayLoggingProvider : ILoggerProvider
{
    public IList<ArrayLoggerEffect> Loggers { get; }
}
