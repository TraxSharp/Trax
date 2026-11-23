using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.Topics.Junctions;

/// <summary>The <c>Unsure</c> track: shared references count for half.</summary>
public class DampenCoCitation(FaultInjector faults, DemoPace pace)
    : WeighCoCitation(faults, pace, "Dampened", 0.5);
