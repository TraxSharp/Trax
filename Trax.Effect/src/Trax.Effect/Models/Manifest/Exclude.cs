using System.Text.Json.Serialization;

namespace Trax.Effect.Models.Manifest;

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
