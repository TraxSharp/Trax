// What the example on sdk-reference/train-methods/switch.md names but does not declare.
using Trax.Core.Decisions;
using Trax.Core.Junction;

public record LoanApplication(string ApplicantId, decimal Amount);

public record LoanOutcome(string Decision);

[Asks("How should this loan application be underwritten?")]
public enum Underwriting
{
    Approve,
    Decline,
    ManualReview,
}

public class MakeOffer : Junction<LoanApplication, LoanOutcome>
{
    public override Task<LoanOutcome> Run(LoanApplication input) =>
        Task.FromResult(new LoanOutcome("offer"));
}

public class SendAdverseActionNotice : Junction<LoanApplication, LoanOutcome>
{
    public override Task<LoanOutcome> Run(LoanApplication input) =>
        Task.FromResult(new LoanOutcome("declined"));
}

public class AssignUnderwriter : Junction<LoanApplication, LoanOutcome>
{
    public override Task<LoanOutcome> Run(LoanApplication input) =>
        Task.FromResult(new LoanOutcome("review"));
}
