using LanguageExt;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Stress.Fakes.Trains;

public class StressTestTrain : ServiceTrain<StressTestInput, Unit>, IStressTestTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(StressTestInput input) =>
        Activate(input, Unit.Default).Resolve();
}

public record StressTestInput : IManifestProperties
{
    public string Value { get; set; } = string.Empty;
}

public interface IStressTestTrain : IServiceTrain<StressTestInput, Unit> { }
