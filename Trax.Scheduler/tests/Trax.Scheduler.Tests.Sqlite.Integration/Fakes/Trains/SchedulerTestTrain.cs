using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Sqlite.Integration.Fakes.Trains;

public class SchedulerTestTrain : ServiceTrain<SchedulerTestInput, Unit>, ISchedulerTestTrain
{
    protected override async Task<Either<Exception, Unit>> Junctions() => Resolve();
}

public record SchedulerTestInput : IManifestProperties
{
    public string Value { get; set; } = string.Empty;
}

public interface ISchedulerTestTrain : IServiceTrain<SchedulerTestInput, Unit> { }

public class FailingSchedulerTestTrain
    : ServiceTrain<FailingSchedulerTestInput, Unit>,
        IFailingSchedulerTestTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<FailWithMessage>().Resolve();
}

public record FailingSchedulerTestInput : IManifestProperties
{
    public string FailureMessage { get; set; } = "Test failure";
}

public interface IFailingSchedulerTestTrain : IServiceTrain<FailingSchedulerTestInput, Unit> { }

/// <summary>Fails with the message the input carries, so the failure path is exercised.</summary>
internal sealed class FailWithMessage : Junction<FailingSchedulerTestInput, Unit>
{
    public override Task<Unit> Run(FailingSchedulerTestInput input) =>
        throw new InvalidOperationException($"Intentional failure: {input.FailureMessage}");
}
