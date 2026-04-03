using LanguageExt;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Sqlite.Integration.Fakes.Trains;

public class SchedulerTestTrain : ServiceTrain<SchedulerTestInput, Unit>, ISchedulerTestTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(SchedulerTestInput input) =>
        Activate(input, Unit.Default).Resolve();
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
    protected override async Task<Either<Exception, Unit>> RunInternal(
        FailingSchedulerTestInput input
    ) => new InvalidOperationException($"Intentional failure: {input.FailureMessage}");
}

public record FailingSchedulerTestInput : IManifestProperties
{
    public string FailureMessage { get; set; } = "Test failure";
}

public interface IFailingSchedulerTestTrain : IServiceTrain<FailingSchedulerTestInput, Unit> { }
