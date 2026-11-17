using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Services.Operations;

/// <summary>The order <see cref="IOperationsService.GetLogsAsync"/> reads log entries in.</summary>
public enum LogOrder
{
    /// <summary>Highest id first: the newest entry first.</summary>
    NewestFirst = 0,

    /// <summary>Lowest id first: the oldest entry first, as a run's log reads.</summary>
    OldestFirst = 1,
}
