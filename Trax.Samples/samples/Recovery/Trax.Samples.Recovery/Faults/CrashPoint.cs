namespace Trax.Samples.Recovery.Faults;

/// <summary>Where an armed fault fires.</summary>
public enum CrashPoint
{
    /// <summary>No crash: the run completes on its first attempt.</summary>
    None,

    /// <summary>
    /// The step the refund's approval decision routes to: paying, declining or queueing for review.
    /// Every order reaches one of the three, so a crash armed here fires whatever the model answers.
    /// </summary>
    RefundTrack,

    /// <summary>
    /// The refund's last step, emailing the customer. It comes after the checkpoint, so the retry starts
    /// from the stored outcome: nothing before it runs again, and the payment is not made twice.
    /// </summary>
    RefundEmail,

    /// <summary>
    /// The step on the track the topic map's co-citation decision routes to, inside the
    /// <c>cocitation</c> branch. The branch fails, so the run fails naming it, and the join after
    /// the branches never writes.
    /// </summary>
    CoCitation,

    /// <summary>
    /// The ingest's upsert, before it writes, keyed by the partition (<c>Source/yyyy-MM</c>) rather
    /// than a run id. Every track of the ingest's gate reaches it, so it fires whatever the model
    /// answered, and the partition is left as it was.
    /// </summary>
    Ingest,

    /// <summary>
    /// The restaurant's tablet, when an order is sent to it: it does not answer in time, once. The order's
    /// <c>SendToRestaurant</c> tries it again, and the second attempt is answered.
    /// </summary>
    TabletTimesOut,

    /// <summary>The restaurant, when an order is sent to it: it is closed, so the order is refused before any charge.</summary>
    RestaurantClosed,
}
