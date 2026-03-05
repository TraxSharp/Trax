using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Trains.JobRunner;

/// <summary>
/// Train interface for executing scheduled train jobs via the manifest system.
/// </summary>
public interface IJobRunnerTrain : IServiceTrain<RunJobRequest, Unit>;
