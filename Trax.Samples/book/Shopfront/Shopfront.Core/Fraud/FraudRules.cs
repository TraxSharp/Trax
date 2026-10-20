using Trax.Core.Decisions;

namespace Shopfront.Fraud;

public static class FraudRules
{
    // A stolen card's first use is usually its largest: hold first orders of 100 or more.
    public static RuleDecider Live() =>
        new RuleDecider().YesNo<OrderRisk, LooksFraudulent>(risk =>
            risk.FirstOrder && risk.Total >= 100m
        );
}
