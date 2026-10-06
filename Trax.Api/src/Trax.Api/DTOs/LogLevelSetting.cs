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

/// <summary>A level to set for one category with <c>config.setLogLevels</c>.</summary>
/// <param name="Category">The category, as configured under <c>Logging:LogLevel</c>; case is ignored.</param>
/// <param name="Level">The level to filter it at.</param>
public record LogLevelSettingInput(string Category, LogLevel Level);

/// <summary>What <c>config.setLogLevels</c> did. A list naming an unknown category sets nothing.</summary>
/// <param name="Success">Whether the levels were set.</param>
/// <param name="Count">How many categories were set; 0 on failure.</param>
/// <param name="NotApplied">
/// Categories set whose loggers still filter at another level, which happens when the host sets
/// the filter itself after Trax. Empty on failure.
/// </param>
/// <param name="Message">One line for an operator.</param>
public record SetLogLevelsResponse(
    bool Success,
    int Count,
    IReadOnlyList<string> NotApplied,
    string Message
);
