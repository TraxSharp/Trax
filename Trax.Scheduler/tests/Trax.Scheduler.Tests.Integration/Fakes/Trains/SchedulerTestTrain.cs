using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>
/// A simple test train for scheduler integration tests.
/// </summary>
public class SchedulerTestTrain : ServiceTrain<SchedulerTestInput, Unit>, ISchedulerTestTrain
{
    protected override async Task<Either<Exception, Unit>> Junctions() => Resolve();
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
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<FailWithMessage>().Resolve();
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

/// <summary>
/// A test train with a typed (non-Unit) output, used to verify the scheduler
/// supports trains that return values other than Unit.
/// </summary>
public class TypedOutputSchedulerTestTrain
    : ServiceTrain<TypedOutputSchedulerTestInput, string>,
        ITypedOutputSchedulerTestTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<ProcessTypedOutput>().Resolve();
}

/// <summary>
/// Input for the typed output scheduler test train.
/// </summary>
public record TypedOutputSchedulerTestInput : IManifestProperties
{
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Interface for the typed output scheduler test train.
/// </summary>
public interface ITypedOutputSchedulerTestTrain
    : IServiceTrain<TypedOutputSchedulerTestInput, string> { }

/// <summary>Fails with the message the input carries, so the failure path is exercised.</summary>
internal sealed class FailWithMessage : Junction<FailingSchedulerTestInput, Unit>
{
    public override Task<Unit> Run(FailingSchedulerTestInput input) =>
        throw new InvalidOperationException($"Intentional failure: {input.FailureMessage}");
}

/// <summary>Produces the typed output these scheduler tests assert on.</summary>
internal sealed class ProcessTypedOutput : Junction<TypedOutputSchedulerTestInput, string>
{
    public override Task<string> Run(TypedOutputSchedulerTestInput input) =>
        Task.FromResult($"processed-{input.Value}");
}
