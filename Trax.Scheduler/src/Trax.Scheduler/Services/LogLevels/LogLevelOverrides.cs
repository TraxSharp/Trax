using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Trax.Scheduler.Services.LogLevels;

/// <summary>
/// Log levels set at runtime through <see cref="ILogLevelService"/>, applied to the host's
/// <see cref="LoggerFilterOptions"/> rather than written into its configuration.
/// </summary>
/// <remarks>
/// A value written into <c>IConfiguration</c> lives in whichever provider accepts it and is lost
/// when that provider reloads, and a source the host added later wins over it. Applying the levels
/// as a post-configure step on the filter options, and signalling a change, puts them over every
/// configuration source and keeps them across a reload of the host's configuration: the options
/// monitor recomputes the filter options on the signal, and the logger factory re-reads its filters
/// from it. They are held in memory and last until the process restarts.
/// </remarks>
internal sealed class LogLevelOverrides
    : IPostConfigureOptions<LoggerFilterOptions>,
        IOptionsChangeTokenSource<LoggerFilterOptions>
{
    /// <summary>The configuration key for the level that applies to every category.</summary>
    public const string DefaultCategory = "Default";

    private readonly ConcurrentDictionary<string, LogLevel> _levels = new(
        StringComparer.OrdinalIgnoreCase
    );
    private CancellationTokenSource _changed = new();

    public string? Name => Options.DefaultName;

    public IReadOnlyDictionary<string, LogLevel> Levels => _levels;

    /// <summary>Sets the levels given and signals the logger factory to re-read its filters.</summary>
    public void Set(IEnumerable<KeyValuePair<string, LogLevel>> levels)
    {
        foreach (var (category, level) in levels)
            _levels[category] = level;

        var previous = Interlocked.Exchange(ref _changed, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }

    public IChangeToken GetChangeToken() => new CancellationChangeToken(_changed.Token);

    public void PostConfigure(string? name, LoggerFilterOptions options)
    {
        foreach (var (category, level) in _levels)
        {
            var filterCategory = ToFilterCategory(category);
            for (var i = options.Rules.Count - 1; i >= 0; i--)
                if (IsCategoryRule(options.Rules[i], filterCategory))
                    options.Rules.RemoveAt(i);
            options.Rules.Add(new LoggerFilterRule(null, filterCategory, level, null));
        }
    }

    /// <summary>
    /// The level the host's loggers apply to a category from its provider-independent rule, or
    /// <c>null</c> when no such rule exists. When several rules match, the last one wins, as in
    /// the logger factory's own selection.
    /// </summary>
    public static LogLevel? EffectiveLevel(LoggerFilterOptions options, string category)
    {
        var filterCategory = ToFilterCategory(category);
        return options.Rules.LastOrDefault(r => IsCategoryRule(r, filterCategory))?.LogLevel;
    }

    private static string? ToFilterCategory(string category) =>
        string.Equals(category, DefaultCategory, StringComparison.OrdinalIgnoreCase)
            ? null
            : category;

    private static bool IsCategoryRule(LoggerFilterRule rule, string? category) =>
        rule.ProviderName is null
        && rule.Filter is null
        && string.Equals(rule.CategoryName, category, StringComparison.OrdinalIgnoreCase);
}
