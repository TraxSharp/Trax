// What the examples on core/write-a-decider.md take as given: the states they decide about and a
// second decider for the cascade.
using System.ComponentModel;
using Trax.Core.Decisions;

public sealed record Ticket(string CustomerId, string Message);

public sealed record LoanApplication(string ApplicantId, int CreditScore, decimal DebtToIncome);

[Asks("How should this loan application be underwritten?")]
public enum Underwriting
{
    [Description("Strong credit and low debt. Make an offer.")]
    Approve,

    [Description("Fails the credit policy. Decline with the required notice.")]
    Decline,

    [Description("Borderline. A human underwriter reviews it.")]
    ManualReview,
}

public sealed class LargeModelDecider : IDecider
{
    public Task<DecisionResult> Decide(
        DecisionRequest request,
        CancellationToken cancellationToken
    ) => Task.FromResult(new DecisionResult(new Dictionary<string, Answer>()));
}
