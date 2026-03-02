using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Trains.TaskServerExecutor;

/// <summary>
/// Train interface for executing scheduled train jobs via the manifest system.
/// </summary>
public interface ITaskServerExecutorTrain : IServiceTrain<ExecuteManifestRequest, Unit>;
