using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.Research.Junctions;

/// <summary>The search on the <c>Wiki</c> track.</summary>
public class SearchWiki(DemoPace pace) : EffectJunction<ResearchBrief, Findings>
{
    public override async Task<Findings> Run(ResearchBrief brief)
    {
        await Task.Delay(pace.StepDelay);
        return new Findings(
            brief.RunId,
            brief.Topic,
            "Wiki",
            ["Our 2024 report tried it", "The platform team wrote a runbook"]
        );
    }
}
