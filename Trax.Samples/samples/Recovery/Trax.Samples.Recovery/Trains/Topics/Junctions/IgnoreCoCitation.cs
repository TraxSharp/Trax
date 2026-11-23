using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.Topics.Junctions;

/// <summary>The <c>No</c> track: shared references do not count.</summary>
public class IgnoreCoCitation(FaultInjector faults, DemoPace pace)
    : WeighCoCitation(faults, pace, "Ignored", 0.0);
