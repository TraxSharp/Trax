using Trax.Effect.Services.ServiceTrain;
using LanguageExt;

namespace Trax.Samples.Scheduler.Workflows.AlwaysFails;

/// <summary>
/// Interface for the AlwaysFails workflow.
/// This workflow always throws an exception, which causes it to dead-letter
/// after exhausting its retry budget—useful for testing dead letter resolution.
/// </summary>
public interface IAlwaysFailsWorkflow : IServiceTrain<AlwaysFailsInput, Unit>;
