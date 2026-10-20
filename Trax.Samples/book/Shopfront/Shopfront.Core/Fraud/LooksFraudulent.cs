using Trax.Core.Decisions;

namespace Shopfront.Fraud;

[Asks(
    "Does this order look like a stolen card being used?",
    Yes = "Hold it: it matches how stolen cards are used.",
    No = "Take it: nothing about it is unusual."
)]
public sealed class LooksFraudulent;
