using LanguageExt;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.Fakes.Trains;

public record RunIdProbeInput(string Marker);

/// <summary>
/// Records what <see cref="JunctionMetadata.TrainMetadataId"/> looked like from inside
/// <c>Run</c>, which is the only place a junction is supposed to read it.
/// </summary>
public class RunIdProbeJunction : EffectJunction<RunIdProbeInput, Unit>
{
    /// <summary>-1 until the junction has run, so "never ran" and "ran and saw 0" stay distinct.</summary>
    public long ObservedTrainMetadataId { get; private set; } = -1;

    public string? ObservedTrainExternalId { get; private set; }

    public override Task<Unit> Run(RunIdProbeInput input)
    {
        ObservedTrainMetadataId = Metadata!.TrainMetadataId;
        ObservedTrainExternalId = Metadata!.TrainExternalId;
        return Task.FromResult(Unit.Default);
    }
}

/// <summary>
/// Chains a junction instance the test holds, so the test can read back what the junction saw
/// without reaching into the container.
/// </summary>
public class RunIdProbeTrain(RunIdProbeJunction junction) : ServiceTrain<RunIdProbeInput, Unit>
{
    public RunIdProbeJunction Junction { get; } = junction;

    protected override Task<Either<Exception, Unit>> Junctions() => Chain(Junction).Resolve();
}
