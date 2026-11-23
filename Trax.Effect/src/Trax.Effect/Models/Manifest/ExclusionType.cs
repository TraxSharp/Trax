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
