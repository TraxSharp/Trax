using Trax.Effect.Services.ServiceTrain;
using LanguageExt;

namespace Trax.Scheduler.Workflows.TaskServerExecutor;

/// <summary>
/// Workflow interface for executing scheduled workflow jobs via the manifest system.
/// </summary>
public interface ITaskServerExecutorWorkflow : IServiceTrain<ExecuteManifestRequest, Unit>;
