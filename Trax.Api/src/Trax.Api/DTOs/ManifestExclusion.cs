using Trax.Effect.Models.Manifest;

namespace Trax.Api.DTOs;

/// <summary>
/// A single manifest schedule exclusion window, mirroring the domain <see cref="Exclusion"/>: a
/// flat discriminated shape where <see cref="Type"/> selects which fields apply. DaysOfWeek is set
/// for <c>DaysOfWeek</c>, Dates for <c>Dates</c>, StartDate/EndDate for <c>DateRange</c>, and
/// StartTime/EndTime for <c>TimeWindow</c>. Backs the exclusions panel on the manifest detail page.
/// </summary>
public record ManifestExclusion(
    ExclusionType Type,
    IReadOnlyList<DayOfWeek>? DaysOfWeek,
    IReadOnlyList<DateOnly>? Dates,
    DateOnly? StartDate,
    DateOnly? EndDate,
    TimeOnly? StartTime,
    TimeOnly? EndTime
);
