using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Trains.StartRun;

/// <summary>Where a refund's crash fires: before its checkpoint or after it.</summary>
public enum RefundCrash
{
    /// <summary>In the step the approval routes to (paying, declining or queueing), before the checkpoint.</summary>
    Paying,

    /// <summary>In <c>NotifyCustomer</c>, after the checkpoint.</summary>
    Emailing,

    /// <summary>
    /// Both, once each: the first attempt crashes paying, its retry replays the answers, pays and crashes emailing,
    /// and the last retry starts from the checkpoint. Takes all three attempts the manifest allows.
    /// </summary>
    PayingThenEmailing,
}
