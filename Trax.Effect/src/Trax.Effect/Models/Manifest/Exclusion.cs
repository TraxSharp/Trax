using System.Text.Json.Serialization;

namespace Trax.Effect.Models.Manifest;

/// <summary>
/// Represents a single exclusion window for a manifest schedule.
/// Uses a flat discriminated model: the <see cref="Type"/> property determines
/// which other properties are meaningful.
/// </summary>
/// <remarks>
/// Multiple exclusions can be combined on a single manifest. If ANY exclusion
/// matches the current time, the manifest is skipped. Excluded periods are
/// treated as "intentionally skipped" — not as misfires.
/// </remarks>
public class Exclusion
{
    /// <summary>The type of exclusion.</summary>
    public ExclusionType Type { get; set; }

    /// <summary>
    /// Days of the week to exclude. Used when <see cref="Type"/> is
    /// <see cref="ExclusionType.DaysOfWeek"/>.
    /// </summary>
    public IList<DayOfWeek>? DaysOfWeek { get; set; }

    /// <summary>
    /// Specific dates to exclude. Used when <see cref="Type"/> is
    /// <see cref="ExclusionType.Dates"/>.
    /// </summary>
    public IList<DateOnly>? Dates { get; set; }

    /// <summary>
    /// Start date of the exclusion range (inclusive). Used when <see cref="Type"/> is
    /// <see cref="ExclusionType.DateRange"/>.
    /// </summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>
    /// End date of the exclusion range (inclusive). Used when <see cref="Type"/> is
    /// <see cref="ExclusionType.DateRange"/>.
    /// </summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// Start time of the daily exclusion window. Used when <see cref="Type"/> is
    /// <see cref="ExclusionType.TimeWindow"/>.
    /// </summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary>
    /// End time of the daily exclusion window. Used when <see cref="Type"/> is
    /// <see cref="ExclusionType.TimeWindow"/>.
    /// </summary>
    public TimeOnly? EndTime { get; set; }

    // ── Evaluation ──────────────────────────────────────────────────

    /// <summary>
    /// Evaluates whether the given UTC time falls within this exclusion window.
    /// </summary>
    public bool IsExcluded(DateTime utcNow)
    {
        return Type switch
        {
            ExclusionType.DaysOfWeek => DaysOfWeek is not null
                && DaysOfWeek.Contains(utcNow.DayOfWeek),

            ExclusionType.Dates => Dates is not null
                && Dates.Contains(DateOnly.FromDateTime(utcNow)),

            ExclusionType.DateRange => StartDate.HasValue
                && EndDate.HasValue
                && DateOnly.FromDateTime(utcNow) >= StartDate.Value
                && DateOnly.FromDateTime(utcNow) <= EndDate.Value,

            ExclusionType.TimeWindow => StartTime.HasValue
                && EndTime.HasValue
                && IsInTimeWindow(TimeOnly.FromDateTime(utcNow), StartTime.Value, EndTime.Value),

            _ => false,
        };
    }

    /// <summary>
    /// Checks if a time falls within a window, handling midnight crossover
    /// (e.g., 23:00–02:00).
    /// </summary>
    private static bool IsInTimeWindow(TimeOnly now, TimeOnly start, TimeOnly end)
    {
        if (start <= end)
            return now >= start && now < end;

        // Window crosses midnight (e.g., 23:00–02:00)
        return now >= start || now < end;
    }
}
