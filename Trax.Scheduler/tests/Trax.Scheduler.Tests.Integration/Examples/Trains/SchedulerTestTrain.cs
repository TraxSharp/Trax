using LanguageExt;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Examples.Trains;

/// <summary>
/// A simple test train for scheduler integration tests.
/// Returns Unit since TaskServerExecutor uses the non-generic RunAsync which expects Unit output.
/// </summary>
public class SchedulerTestTrain : ServiceTrain<SchedulerTestInput, Unit>, ISchedulerTestTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(SchedulerTestInput input) =>
        Activate(input, Unit.Default).Resolve();
}

/// <summary>
/// Input for the scheduler test train.
/// </summary>
public record SchedulerTestInput : IManifestProperties
{
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Interface for the scheduler test train.
/// </summary>
public interface ISchedulerTestTrain : IServiceTrain<SchedulerTestInput, Unit> { }

/// <summary>
/// A train that always fails, used for testing error handling.
/// </summary>
public class FailingSchedulerTestTrain
    : ServiceTrain<FailingSchedulerTestInput, Unit>,
        IFailingSchedulerTestTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        FailingSchedulerTestInput input
    ) => new InvalidOperationException($"Intentional failure: {input.FailureMessage}");
}

/// <summary>
/// Input for the failing scheduler test train.
/// </summary>
public record FailingSchedulerTestInput : IManifestProperties
{
    public string FailureMessage { get; set; } = "Test failure";
}

/// <summary>
/// Interface for the failing scheduler test train.
/// </summary>
public interface IFailingSchedulerTestTrain : IServiceTrain<FailingSchedulerTestInput, Unit> { }
