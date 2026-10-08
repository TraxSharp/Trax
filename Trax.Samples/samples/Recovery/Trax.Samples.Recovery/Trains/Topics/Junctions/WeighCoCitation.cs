using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.Topics.Junctions;

/// <summary>
/// The step on each track of the co-citation gate: it sets how much the map trusts shared
/// references. This is the step the page can crash. It fails after the model has answered, inside
/// the branch, so the run fails with the branch named and the join never writes.
/// </summary>
public abstract class WeighCoCitation(
    FaultInjector faults,
    DemoPace pace,
    string track,
    double weight
) : EffectJunction<CoCitationEvidence, CoCitationSignal>
{
    public override async Task<CoCitationSignal> Run(CoCitationEvidence evidence)
    {
        await Task.Delay(pace.StepDelay, CancellationToken);

        if (faults.TryFire(evidence.RunId, CrashPoint.CoCitation))
            throw new TimeoutException(
                "The citation index did not answer in time (crash injected by the demo)."
            );

        return new CoCitationSignal(track, weight, evidence.Pairs);
    }
}

/// <summary>The <c>Yes</c> track: shared references count in full.</summary>
public class TrustCoCitation(FaultInjector faults, DemoPace pace)
    : WeighCoCitation(faults, pace, "Trusted", 1.0);

/// <summary>The <c>Unsure</c> track: shared references count for half.</summary>
public class DampenCoCitation(FaultInjector faults, DemoPace pace)
    : WeighCoCitation(faults, pace, "Dampened", 0.5);

/// <summary>The <c>No</c> track: shared references do not count.</summary>
public class IgnoreCoCitation(FaultInjector faults, DemoPace pace)
    : WeighCoCitation(faults, pace, "Ignored", 0.0);
