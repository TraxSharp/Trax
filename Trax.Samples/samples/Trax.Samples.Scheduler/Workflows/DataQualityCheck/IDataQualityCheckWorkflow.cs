using Trax.Effect.Services.ServiceTrain;
using LanguageExt;

namespace Trax.Samples.Scheduler.Workflows.DataQualityCheck;

/// <summary>
/// Interface for the DataQualityCheck workflow.
/// Used by the WorkflowBus for workflow resolution.
/// </summary>
public interface IDataQualityCheckWorkflow : IServiceTrain<DataQualityCheckInput, Unit>;
