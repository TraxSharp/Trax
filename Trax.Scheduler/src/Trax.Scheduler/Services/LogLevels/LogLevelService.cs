using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Trax.Scheduler.Services.LogLevels;

/// <inheritdoc />
/// <param name="overrides">The levels set at runtime, applied to the logger filter options.</param>
/// <param name="configuration">The host's configuration; null when it registered none.</param>
/// <param name="filters">The logger filter options; null when the host registered no logging.</param>
internal sealed class LogLevelService(
    LogLevelOverrides overrides,
    IConfiguration? configuration,
    IOptionsMonitor<LoggerFilterOptions>? filters
) : ILogLevelService
{
    private const string Section = "Logging:LogLevel";

    /// <inheritdoc />
    public IReadOnlyList<CategoryLogLevel> GetLogLevels()
    {
        var configured = Configured();
        var current = filters?.CurrentValue;
        var set = overrides.Levels;

        return configured
            .Keys.Concat(set.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(category =>
            {
                var configuredValue = configured.GetValueOrDefault(category);
                var overridden = set.TryGetValue(category, out var setLevel);
                var level =
                    (current is null ? null : LogLevelOverrides.EffectiveLevel(current, category))
                    ?? (overridden ? setLevel : (LogLevel?)null)
                    ?? Parse(configuredValue)
                    ?? LogLevel.Information;
                return new CategoryLogLevel(category, level, configuredValue, overridden);
            })
            .OrderBy(e => IsDefault(e.Category) ? 0 : 1)
            .ThenBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public LogLevelUpdateResult SetLogLevels(IReadOnlyList<LogLevelChange> levels)
    {
        if (levels.Count == 0)
            return Refused("No levels were given.");

        var undefined = levels.Where(l => !Enum.IsDefined(l.Level)).ToList();
        if (undefined.Count > 0)
            return Refused(
                "Not a log level: "
                    + string.Join(", ", undefined.Select(l => $"{(int)l.Level} for {l.Category}"))
                    + "."
            );

        // Only a level the host configures, or one set here before, can be set: a list cannot
        // turn on logging the host never chose to configure.
        var known = Configured()
            .Keys.Concat(overrides.Levels.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = levels
            .Select(l => l.Category)
            .Where(c => string.IsNullOrWhiteSpace(c) || !known.Contains(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unknown.Count > 0)
            return Refused(
                $"Not a category configured under {Section}: {string.Join(", ", unknown)}."
            );

        var changes = levels
            .GroupBy(l => l.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => KeyValuePair.Create(g.Key, g.Last().Level))
            .ToList();
        overrides.Set(changes);

        // Read back what the loggers now filter at: the host can set the filter itself after Trax.
        var notApplied = filters is null
            ? []
            : changes
                .Where(c =>
                    LogLevelOverrides.EffectiveLevel(filters.CurrentValue, c.Key) != c.Value
                )
                .Select(c => c.Key)
                .ToList();

        var message = $"{changes.Count} log level(s) set in this process.";
        if (notApplied.Count > 0)
            message +=
                $" The host filters {string.Join(", ", notApplied)} at another level, so the change "
                + "does not apply there.";

        return new LogLevelUpdateResult(true, changes.Count, notApplied, message);
    }

    private static LogLevelUpdateResult Refused(string message) => new(false, 0, [], message);

    /// <summary>The categories configured under <c>Logging:LogLevel</c>, with their values as written.</summary>
    private Dictionary<string, string?> Configured()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (configuration is null)
            return result;

        foreach (var section in configuration.GetSection(Section).GetChildren())
            result[section.Key] = section.Value;

        return result;
    }

    private static LogLevel? Parse(string? value) =>
        Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level) && Enum.IsDefined(level)
            ? level
            : null;

    private static bool IsDefault(string category) =>
        string.Equals(
            category,
            LogLevelOverrides.DefaultCategory,
            StringComparison.OrdinalIgnoreCase
        );
}
