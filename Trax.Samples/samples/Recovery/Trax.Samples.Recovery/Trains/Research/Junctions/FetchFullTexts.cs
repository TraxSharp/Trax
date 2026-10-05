using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.Research.Junctions;

/// <summary>The step on the <c>CrossCheck</c> track: the findings are checked against the full texts.</summary>
public class FetchFullTexts(DemoPace pace) : EffectJunction<Findings, CheckedFindings>
{
    public override async Task<CheckedFindings> Run(Findings findings)
    {
        await Task.Delay(pace.StepDelay);

        return new CheckedFindings(findings, nameof(Depth.CrossCheck));
    }
}
