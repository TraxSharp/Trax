using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.Topics.Junctions;

/// <summary>
/// Finds the hidden twins: papers that read alike (an embedding similarity of
/// <see cref="Similarity"/> or more) but cite nothing in common, so a citation graph would never put
/// them side by side. The run is over after this, so anything still armed for it is removed.
/// </summary>
public class FindHiddenTwins(FaultInjector faults, DemoPace pace)
    : EffectJunction<CombinedSignals, TopicMap>
{
    /// <summary>How alike two abstracts must read to be twins.</summary>
    public const double Similarity = 0.35;

    public override async Task<TopicMap> Run(CombinedSignals combined)
    {
        await Task.Delay(pace.StepDelay, CancellationToken);
        faults.Disarm(combined.RunId);

        return new TopicMap(
            combined.Papers,
            combined.Written,
            combined.CoCitationTrack,
            combined
                .Links.Where(l => l.Score >= CombineSignals.Threshold)
                .OrderByDescending(l => l.Score)
                .ThenBy(l => l.WorkA, StringComparer.Ordinal)
                .ThenBy(l => l.WorkB, StringComparer.Ordinal)
                .Take(5)
                .ToList(),
            combined
                .Links.Where(l => l.Embedding >= Similarity && l.SharedReferences == 0)
                .OrderByDescending(l => l.Embedding)
                .ThenBy(l => l.WorkA, StringComparer.Ordinal)
                .ToList()
        );
    }
}
