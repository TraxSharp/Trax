using Trax.Effect.Data.Enums;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Data.Services.DataContextLoggingProvider;

public interface IDataContextLoggingProviderConfiguration
{
    public LogLevel MinimumLogLevel { get; }

    public List<string> Blacklist { get; }
}

public class DataContextLoggingProviderConfiguration : IDataContextLoggingProviderConfiguration
{
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;

    public List<string> Blacklist { get; set; } = [];
}
