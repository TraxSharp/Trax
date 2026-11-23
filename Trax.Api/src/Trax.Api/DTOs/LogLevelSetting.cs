using Microsoft.Extensions.Logging;

namespace Trax.Api.DTOs;

/// <summary>
/// One log category the host configures under <c>Logging:LogLevel</c> (or has changed at runtime),
/// and the level its loggers filter at.
/// </summary>
/// <param name="Category">The logging category, <c>Default</c> for the fallback.</param>
/// <param name="Level">
/// The level the category's loggers filter at now, by its <see cref="LogLevel"/> name: a level
/// set at runtime when there is one, otherwise the configured one.
/// </param>
public record LogLevelSetting(string Category, string Level)
{
    /// <summary>
    /// The value configured under <c>Logging:LogLevel</c>, as written; null when the category is
    /// not configured there.
    /// </summary>
    public string? ConfiguredLevel { get; init; }

    /// <summary>
    /// Whether a level was set at runtime with <c>config.setLogLevels</c>, so <see cref="Level"/>
    /// is not the configured one. Lasts until the process restarts.
    /// </summary>
    public bool Overridden { get; init; }
}
