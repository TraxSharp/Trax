using Trax.Core.Decisions;

namespace Shopfront.Fraud;

// Holds first orders from 60. Asked beside the live rule, never acted on.
public sealed class StricterFraudRules : ICandidateFraudDecider
{
    private readonly RuleDecider rules = new RuleDecider().YesNo<OrderRisk, LooksFraudulent>(risk =>
        risk.FirstOrder && risk.Total >= 60m
    );

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken) =>
        rules.Decide(request, cancellationToken);
}
