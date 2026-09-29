using Microsoft.Extensions.Logging;

namespace Trax.Effect.Data.Services.DataContextLoggingProvider;

/// <summary>
/// Settings for <see cref="DataContextLoggingProvider"/>. Consumers set them through the parameters
/// of <c>AddDataContextLogging</c>; not intended to be implemented directly.
/// </summary>
public interface IDataContextLoggingProviderConfiguration
{
    /// <summary>The lowest level stored; entries below it are ignored.</summary>
    public LogLevel MinimumLogLevel { get; }

    /// <summary>
    /// Logger categories whose entries are never stored. An entry containing <c>*</c> is a wildcard
    /// matched against the whole category (for example <c>Microsoft.EntityFrameworkCore.*</c>); any
    /// other entry must match exactly and case-sensitively. Read once, when the provider is built.
    /// </summary>
    public List<string> Blacklist { get; }
}

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
    public List<string> Blacklist { get; set; } = [];
}
