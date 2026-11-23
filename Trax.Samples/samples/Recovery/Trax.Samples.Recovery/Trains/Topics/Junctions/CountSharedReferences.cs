using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.Topics.Junctions;

/// <summary>
/// The <c>cocitation</c> branch's first step: for every two papers, how many works they both cite.
/// The model is then asked whether that means anything in this slice.
/// </summary>
public class CountSharedReferences(DemoPace pace) : EffectJunction<CorpusSlice, CoCitationEvidence>
{
    public override async Task<CoCitationEvidence> Run(CorpusSlice slice)
    {
        await Task.Delay(pace.StepDelay, CancellationToken).ConfigureAwait(false);

        var pairs = PaperPairs
            .Of(slice.Papers)
            .Select(pair => new PairSignal(
                pair.A.Id,
                pair.B.Id,
                PaperPairs.Shared(pair.A.References, pair.B.References)
            ))
            .Where(pair => pair.Value > 0)
            .ToList();

        var linked = pairs
            .SelectMany(p => new[] { p.WorkA, p.WorkB })
            .Distinct(StringComparer.Ordinal)
            .Count();
        return new CoCitationEvidence(slice.RunId, slice.Papers.Count, linked, pairs);
    }
}
