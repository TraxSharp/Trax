using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.Topics.Junctions;

/// <summary>The <c>authors</c> branch: for every two papers, how many authors wrote both.</summary>
public class AuthorOverlap(DemoPace pace) : EffectJunction<CorpusSlice, AuthorSignal>
{
    public override async Task<AuthorSignal> Run(CorpusSlice slice)
    {
        await Task.Delay(pace.StepDelay, CancellationToken);

        return new AuthorSignal(
            PaperPairs
                .Of(slice.Papers)
                .Select(pair => new PairSignal(
                    pair.A.Id,
                    pair.B.Id,
                    PaperPairs.Shared(pair.A.Authors, pair.B.Authors)
                ))
                .Where(pair => pair.Value > 0)
                .ToList()
        );
    }
}
