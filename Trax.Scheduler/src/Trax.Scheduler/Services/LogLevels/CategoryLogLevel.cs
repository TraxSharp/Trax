using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Services.LogLevels;

/// <summary>A configured log category and the level its loggers filter at.</summary>
/// <param name="Category">The category, <c>Default</c> for the level every other category falls back to.</param>
/// <param name="Level">
/// The level the loggers apply now: a level set at runtime when there is one, otherwise the
/// configured one, <c>Information</c> when the configured value is not a level.
/// </param>
/// <param name="ConfiguredLevel">
/// The value configured under <c>Logging:LogLevel</c>, as written; null when the category is not
/// configured there.
/// </param>
/// <param name="Overridden">Whether a level was set at runtime, so <paramref name="Level"/> is not the configured one.</param>
public record CategoryLogLevel(
    string Category,
    LogLevel Level,
    string? ConfiguredLevel,
    bool Overridden
);
