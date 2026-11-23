using System.ComponentModel;
using Trax.Core.Decisions;
using Trax.Effect.Attributes;

namespace Trax.Samples.Recovery.Trains.Refund;

[Asks(
    "Should this refund be paid without a person reviewing it?",
    Yes = "Pay it now: the amount is modest and the customer's history is clean.",
    No = "Decline it: the request does not qualify for a refund."
)]
public sealed class ApproveRefund;
