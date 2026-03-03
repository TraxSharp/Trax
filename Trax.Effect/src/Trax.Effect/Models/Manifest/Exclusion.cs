using System.Text.Json.Serialization;

namespace Trax.Effect.Models.Manifest;

/// <summary>
/// Defines the kind of exclusion window.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExclusionType
{
    /// <summary>Exclude specific days of the week (e.g., weekends).</summary>
    DaysOfWeek = 0,

    /// <summary>Exclude specific dates (e.g., holidays).</summary>
    Dates = 1,

    /// <summary>Exclude a contiguous date range (start–end inclusive).</summary>
    DateRange = 2,

    /// <summary>Exclude a daily time window (e.g., 2am–4am maintenance).</summary>
    TimeWindow = 3,
}

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
    public List<DayOfWeek>? DaysOfWeek { get; set; }

    /// <summary>
    /// Specific dates to exclude. Used when <see cref="Type"/> is
    /// <see cref="ExclusionType.Dates"/>.
    /// </summary>
    public List<DateOnly>? Dates { get; set; }

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

/// <summary>
/// Static factory methods for creating <see cref="Exclusion"/> instances.
/// </summary>
/// <example>
/// <code>
/// options => options
///     .Exclude(Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday))
///     .Exclude(Exclude.Dates(new DateOnly(2026, 12, 25)))
///     .Exclude(Exclude.TimeWindow(TimeOnly.Parse("02:00"), TimeOnly.Parse("04:00")))
/// </code>
/// </example>
public static class Exclude
{
    /// <summary>
    /// Creates an exclusion that skips specific days of the week.
    /// </summary>
    public static Exclusion DaysOfWeek(params DayOfWeek[] days) =>
        new() { Type = ExclusionType.DaysOfWeek, DaysOfWeek = [.. days] };

    /// <summary>
    /// Creates an exclusion that skips specific dates.
    /// </summary>
    public static Exclusion Dates(params DateOnly[] dates) =>
        new() { Type = ExclusionType.Dates, Dates = [.. dates] };

    /// <summary>
    /// Creates an exclusion that skips a contiguous date range (start–end inclusive).
    /// </summary>
    public static Exclusion DateRange(DateOnly start, DateOnly end) =>
        new()
        {
            Type = ExclusionType.DateRange,
            StartDate = start,
            EndDate = end,
        };

    /// <summary>
    /// Creates an exclusion that skips a daily time window.
    /// Supports midnight crossover (e.g., 23:00–02:00).
    /// </summary>
    public static Exclusion TimeWindow(TimeOnly start, TimeOnly end) =>
        new()
        {
            Type = ExclusionType.TimeWindow,
            StartTime = start,
            EndTime = end,
        };
}
