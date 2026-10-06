using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Services.LogLevels;

/// <summary>
/// Reads and changes the log level of each category the host configures under
/// <c>Logging:LogLevel</c>, at runtime. The dashboard's server settings and the GraphQL API's log
/// levels query and mutation both call it, so the two read the same levels and change them the
/// same way (central <c>docs/0022</c>).
/// </summary>
/// <remarks>
/// A change is applied to the host's logger filter options, over every configuration source, and
/// lasts until the process restarts; nothing is persisted. It is per process: it changes the
/// logging of the process that served the call, not of the other processes of the deployment.
/// </remarks>
public interface ILogLevelService
{
    /// <summary>
    /// One entry per category configured under <c>Logging:LogLevel</c> (and per category changed
    /// here since), <c>Default</c> first and the rest by name, with the level its loggers filter
    /// at now.
    /// </summary>
    IReadOnlyList<CategoryLogLevel> GetLogLevels();

    /// <summary>
    /// Sets the level of each category given, then reads back the level each is filtered at.
    /// </summary>
    /// <remarks>
    /// Only a category configured under <c>Logging:LogLevel</c>, or changed here before, can be
    /// set: a list naming any other, or a level that is not a <see cref="LogLevel"/> member, is
    /// refused whole. When a category is given twice, the last level wins.
    /// </remarks>
    /// <returns>
    /// Success with <see cref="LogLevelUpdateResult.Count"/> categories set and, in
    /// <see cref="LogLevelUpdateResult.NotApplied"/>, any whose loggers still filter at another
    /// level, which happens when the host sets the filter itself after Trax. A failure, with
    /// nothing set, for an empty list, an unknown category or an undefined level.
    /// </returns>
    LogLevelUpdateResult SetLogLevels(IReadOnlyList<LogLevelChange> levels);
}

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

/// <summary>A level to set for a category.</summary>
/// <param name="Category">The category, as configured under <c>Logging:LogLevel</c>; case is ignored.</param>
/// <param name="Level">The level to filter it at.</param>
public record LogLevelChange(string Category, LogLevel Level);

/// <summary>What <see cref="ILogLevelService.SetLogLevels"/> did.</summary>
/// <param name="Success">Whether the levels were set.</param>
/// <param name="Count">How many categories were set; 0 on failure.</param>
/// <param name="NotApplied">
/// The categories set whose loggers still filter at another level. Empty on failure.
/// </param>
/// <param name="Message">One line for an operator. Always set.</param>
public record LogLevelUpdateResult(
    bool Success,
    int Count,
    IReadOnlyList<string> NotApplied,
    string Message
);
