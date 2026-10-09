using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.Research.Junctions;

/// <summary>
/// Writes the report from whatever the tracks found. This is the step the page can crash: it comes
/// after the checkpoint, so the retry resumes here and runs only this step, from the stored findings.
/// Otherwise the run is over, so anything still armed for it is removed.
/// </summary>
public class Summarize(FaultInjector faults, DemoPace pace)
    : EffectJunction<CheckedFindings, ResearchReport>
{
    public override async Task<ResearchReport> Run(CheckedFindings input)
    {
        await Task.Delay(pace.StepDelay);
        var findings = input.Findings;

        if (faults.TryFire(findings.RunId, CrashPoint.Report))
            throw new TimeoutException(
                "The report store did not answer in time (crash injected by the demo)."
            );

        faults.Disarm(findings.RunId);
        return new ResearchReport(
            findings.Topic,
            findings.Source,
            input.Depth,
            $"{findings.Topic}: {string.Join("; ", findings.Notes)}."
        );
    }
}
