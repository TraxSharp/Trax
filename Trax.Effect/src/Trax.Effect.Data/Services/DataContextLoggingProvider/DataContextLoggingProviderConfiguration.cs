using Microsoft.Extensions.Logging;

namespace Trax.Effect.Data.Services.DataContextLoggingProvider;

/// <summary>
/// The settings object <c>AddDataContextLogging</c> registers. Infrastructure; not intended for
/// direct use.
/// </summary>
public class DataContextLoggingProviderConfiguration : IDataContextLoggingProviderConfiguration
{
    /// <inheritdoc/>
    /// <remarks>Defaults to <see cref="LogLevel.Information"/>.</remarks>
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;

    /// <inheritdoc/>
    /// <remarks>Defaults to empty.</remarks>
    public IReadOnlyList<string> Blacklist { get; set; } = [];
}
