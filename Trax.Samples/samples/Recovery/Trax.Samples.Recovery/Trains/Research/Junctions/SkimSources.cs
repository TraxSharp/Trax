using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.Research.Junctions;

/// <summary>The step on the <c>Skim</c> track: the findings are enough as they are.</summary>
public class SkimSources(DemoPace pace) : EffectJunction<Findings, CheckedFindings>
{
    public override async Task<CheckedFindings> Run(Findings findings)
    {
        await Task.Delay(pace.StepDelay);

        return new CheckedFindings(findings, nameof(Depth.Skim));
    }
}
