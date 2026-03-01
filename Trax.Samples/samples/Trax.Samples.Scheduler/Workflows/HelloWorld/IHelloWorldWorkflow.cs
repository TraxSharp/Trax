using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Scheduler.Workflows.HelloWorld;

/// <summary>
/// Interface for the HelloWorld workflow.
/// Used by the WorkflowBus for workflow resolution.
/// </summary>
public interface IHelloWorldWorkflow : IServiceTrain<HelloWorldInput, Unit>;
