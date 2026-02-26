using Trax.Effect.Services.ServiceTrain;
using LanguageExt;

namespace Trax.Samples.Scheduler.Workflows.GoodbyeWorld;

/// <summary>
/// Interface for the GoodbyeWorld workflow.
/// Used by the WorkflowBus for workflow resolution.
/// </summary>
public interface IGoodbyeWorldWorkflow : IServiceTrain<GoodbyeWorldInput, Unit>;
