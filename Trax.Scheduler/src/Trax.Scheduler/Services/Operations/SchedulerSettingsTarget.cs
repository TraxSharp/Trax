using Trax.Effect.Models.SchedulerConfig;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// The settings one host can apply: the scheduler configuration, and the local worker options
/// when the host runs local workers.
/// </summary>
internal sealed record SchedulerSettingsTarget(
    SchedulerConfiguration Configuration,
    LocalWorkerOptions? Workers
);
