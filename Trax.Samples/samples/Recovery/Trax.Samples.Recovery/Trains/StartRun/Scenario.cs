using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.StartRun;

public enum Scenario
{
    /// <summary>The refund: a switch, a gate and a checkpoint; a payment or an email that times out.</summary>
    Refund,

    /// <summary>The topic map: three signals in parallel branches, a branch that crashes.</summary>
    TopicMap,
}
