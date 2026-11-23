using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.Topics.Junctions;

/// <summary>The <c>Yes</c> track: shared references count in full.</summary>
public class TrustCoCitation(FaultInjector faults, DemoPace pace)
    : WeighCoCitation(faults, pace, "Trusted", 1.0);
