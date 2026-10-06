using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.Research.Junctions;

/// <summary>The search on the <c>Web</c> track.</summary>
public class SearchWeb(DemoPace pace) : EffectJunction<ResearchBrief, Findings>
{
    public override async Task<Findings> Run(ResearchBrief brief)
    {
        await Task.Delay(pace.StepDelay);
        return new Findings(
            brief.RunId,
            brief.Topic,
            "Web",
            ["Three vendor pages compare it", "A news piece from this spring"]
        );
    }
}
