using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.Fakes.Trains;

/// <summary>
/// Chains a junction instance the test holds, so the test can read back what the junction saw
/// without reaching into the container.
/// </summary>
public class RunIdProbeTrain(RunIdProbeJunction junction) : ServiceTrain<RunIdProbeInput, Unit>
{
    public RunIdProbeJunction Junction { get; } = junction;

    protected override Task<Either<Exception, Unit>> Junctions() => Chain(Junction).Resolve();
}
