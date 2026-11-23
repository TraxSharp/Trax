using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Trains.ManifestManager.Utilities;

/// <summary>
/// An InProgress run as the timeout canceller and the stale reaper read it.
/// </summary>
internal sealed record TimedRun(
    long Id,
    long? ParentId,
    string Name,
    DateTime StartTime,
    long? ManifestId,
    int? ManifestTimeoutSeconds
);
