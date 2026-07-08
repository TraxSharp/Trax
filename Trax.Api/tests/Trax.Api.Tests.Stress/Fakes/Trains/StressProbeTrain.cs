using LanguageExt;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.Stress.Fakes.Trains;

/// <summary>
/// Minimal train so <c>AddMediator</c> has an assembly with a real train to discover
/// and the <c>operations.trains</c> endpoint returns a non-empty registry under load.
/// Never executed by the stress suite (the suite only reads).
/// </summary>
public class StressProbeTrain : ServiceTrain<StressProbeInput, Unit>, IStressProbeTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(StressProbeInput input) =>
        Activate(input, Unit.Default).Resolve();
}

public record StressProbeInput : IManifestProperties
{
    public string Value { get; set; } = string.Empty;
}

public interface IStressProbeTrain : IServiceTrain<StressProbeInput, Unit> { }
