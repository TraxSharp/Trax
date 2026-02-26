using Trax.Effect.Services.ServiceTrain;
using LanguageExt;

namespace Trax.Samples.Server.Workflows.HelloWorld;

/// <summary>
/// Interface for the HelloWorld workflow.
/// Used by the WorkflowBus for workflow resolution.
/// </summary>
public interface IHelloWorldWorkflow : IServiceTrain<HelloWorldInput, Unit>;
