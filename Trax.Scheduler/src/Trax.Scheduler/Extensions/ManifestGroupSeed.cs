using Microsoft.EntityFrameworkCore;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Trains.ManifestManager.Utilities;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// The group settings one seeding call states. A null field (or an unstated limit) leaves an
/// existing group's value alone.
/// </summary>
/// <param name="Priority">The group priority the options state, or null.</param>
/// <param name="MaxActiveJobsStated">Whether the options state the limit; its value may be null (no limit).</param>
/// <param name="MaxActiveJobs">The stated limit, when <paramref name="MaxActiveJobsStated"/>.</param>
/// <param name="IsEnabled">The enabled flag the options state, or null.</param>
/// <param name="PriorityIfNew">The priority a new group takes when none is stated.</param>
internal readonly record struct ManifestGroupSeed(
    int? Priority = null,
    bool MaxActiveJobsStated = false,
    int? MaxActiveJobs = null,
    bool? IsEnabled = null,
    int PriorityIfNew = 0
);
