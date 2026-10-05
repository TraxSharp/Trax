using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Records;

namespace Trax.Samples.Recovery.Trains.Research.Junctions;

/// <summary>
/// Builds the brief the first model call is about. Everything the model reads is in the brief, and
/// nothing in it changes between attempts unless its data does: no timestamp, no random id. That is
/// what lets the retry's state hash match the first attempt's.
/// </summary>
public class PlanResearch(CaseFiles caseFiles, DemoPace pace)
    : EffectJunction<ResearchInput, ResearchBrief>
{
    public override async Task<ResearchBrief> Run(ResearchInput input)
    {
        await Task.Delay(pace.StepDelay);
        return new ResearchBrief(input.RunId, input.Topic, caseFiles.AudienceFor(input.RunId));
    }
}
