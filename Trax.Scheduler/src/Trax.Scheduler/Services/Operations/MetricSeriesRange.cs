namespace Trax.Scheduler.Services.Operations;

/// <summary>The window a <see cref="MetricSeries"/> covers, and so its bucket size.</summary>
public enum MetricSeriesRange
{
    /// <summary>The last hour, 60 buckets of one minute.</summary>
    LastHour = 0,

    /// <summary>The last 24 hours, 96 buckets of 15 minutes.</summary>
    Last24Hours = 1,

    /// <summary>The last 7 days, 168 buckets of one hour.</summary>
    Last7Days = 2,

    /// <summary>
    /// The last 30 days, 120 buckets of 6 hours. Refused today by
    /// <see cref="OperationsService.MetricSeriesRefusal"/>: the existing indexes cannot serve it
    /// inside the metrics budget at a million runs.
    /// </summary>
    Last30Days = 3,
}
