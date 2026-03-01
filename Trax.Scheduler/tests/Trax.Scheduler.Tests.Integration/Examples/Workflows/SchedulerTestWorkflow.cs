using LanguageExt;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Examples.Workflows;

/// <summary>
/// A simple test workflow for scheduler integration tests.
/// Returns Unit since TaskServerExecutor uses the non-generic RunAsync which expects Unit output.
/// </summary>
public class SchedulerTestWorkflow : ServiceTrain<SchedulerTestInput, Unit>, ISchedulerTestWorkflow
{
    protected override async Task<Either<Exception, Unit>> RunInternal(SchedulerTestInput input) =>
        Activate(input, Unit.Default).Resolve();
}

/// <summary>
/// Input for the scheduler test workflow.
/// </summary>
public record SchedulerTestInput : IManifestProperties
{
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Interface for the scheduler test workflow.
/// </summary>
public interface ISchedulerTestWorkflow : IServiceTrain<SchedulerTestInput, Unit> { }

/// <summary>
/// A workflow that always fails, used for testing error handling.
/// </summary>
public class FailingSchedulerTestWorkflow
    : ServiceTrain<FailingSchedulerTestInput, Unit>,
        IFailingSchedulerTestWorkflow
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        FailingSchedulerTestInput input
    ) => new InvalidOperationException($"Intentional failure: {input.FailureMessage}");
}

/// <summary>
/// Input for the failing scheduler test workflow.
/// </summary>
public record FailingSchedulerTestInput : IManifestProperties
{
    public string FailureMessage { get; set; } = "Test failure";
}

/// <summary>
/// Interface for the failing scheduler test workflow.
/// </summary>
public interface IFailingSchedulerTestWorkflow : IServiceTrain<FailingSchedulerTestInput, Unit> { }
