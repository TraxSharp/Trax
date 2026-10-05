using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.Research.Junctions;

/// <summary>The search on the <c>Papers</c> track.</summary>
public class SearchPapers(DemoPace pace) : EffectJunction<ResearchBrief, Findings>
{
    public override async Task<Findings> Run(ResearchBrief brief)
    {
        await Task.Delay(pace.StepDelay);
        return new Findings(
            brief.RunId,
            brief.Topic,
            "Papers",
            ["A 2025 survey covers it", "Two preprints disagree on the numbers"]
        );
    }
}
