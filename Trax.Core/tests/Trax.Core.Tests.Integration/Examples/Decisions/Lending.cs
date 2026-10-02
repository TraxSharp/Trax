using LanguageExt;
using Trax.Core.Decisions;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Integration.Examples.Decisions;

// Underwriting a loan application. Every outcome has consequences (money lent, a regulated
// adverse-action notice sent), so the switch declares no Otherwise: if the decider is not sure, the
// run fails and nothing is done, instead of the application being pushed down a default track.

public sealed record LoanApplication(string ApplicantId, int CreditScore, decimal DebtToIncome);

public sealed record LoanDecision(string ApplicantId, string Outcome);

[Asks("How should this loan application be underwritten?")]
public enum Underwriting
{
    [System.ComponentModel.Description("Strong credit and low debt. Make an offer.")]
    Approve,

    [System.ComponentModel.Description(
        "Fails the credit policy. Decline with the required notice."
    )]
    Decline,

    [System.ComponentModel.Description("Borderline. A human underwriter reviews it.")]
    ManualReview,
}

public class UnderwriteLoan(IDecider decider) : Train<LoanApplication, LoanDecision>
{
    protected override Task<Either<Exception, LoanDecision>> Junctions() =>
        AddServices(decider)
            .Switch<LoanApplication, Underwriting>(tracks =>
                tracks
                    .When(Underwriting.Approve, t => t.Chain<MakeOffer>(), requireConfidence: 0.95)
                    .When(
                        Underwriting.Decline,
                        t => t.Chain<SendAdverseActionNotice>(),
                        requireConfidence: 0.95
                    )
                    .When(Underwriting.ManualReview, t => t.Chain<AssignUnderwriter>())
            )
            .Resolve();
}

/// <summary>The same train, expecting its decider from the container rather than from AddServices.</summary>
public class UnderwriteLoanFromContainer : Train<LoanApplication, LoanDecision>
{
    protected override Task<Either<Exception, LoanDecision>> Junctions() =>
        Switch<LoanApplication, Underwriting>(tracks =>
                tracks
                    .When(Underwriting.Approve, t => t.Chain<MakeOffer>())
                    .When(Underwriting.Decline, t => t.Chain<SendAdverseActionNotice>())
                    .When(Underwriting.ManualReview, t => t.Chain<AssignUnderwriter>())
            )
            .Resolve();
}

public class MakeOffer : Junction<LoanApplication, LoanDecision>
{
    public override Task<LoanDecision> Run(LoanApplication input) =>
        Task.FromResult(new LoanDecision(input.ApplicantId, "offer made"));
}

public class SendAdverseActionNotice : Junction<LoanApplication, LoanDecision>
{
    public override Task<LoanDecision> Run(LoanApplication input) =>
        Task.FromResult(new LoanDecision(input.ApplicantId, "declined, notice sent"));
}

public class AssignUnderwriter : Junction<LoanApplication, LoanDecision>
{
    public override Task<LoanDecision> Run(LoanApplication input) =>
        Task.FromResult(new LoanDecision(input.ApplicantId, "assigned to an underwriter"));
}

public static class CreditPolicy
{
    /// <summary>The written credit policy. A decider need not be a model.</summary>
    public static RuleDecider Decider() =>
        new RuleDecider().Choice<LoanApplication, Underwriting>(application =>
            application switch
            {
                { CreditScore: >= 740, DebtToIncome: <= 0.36m } => Underwriting.Approve,
                { CreditScore: < 580 } or { DebtToIncome: > 0.5m } => Underwriting.Decline,
                _ => Underwriting.ManualReview,
            }
        );
}
