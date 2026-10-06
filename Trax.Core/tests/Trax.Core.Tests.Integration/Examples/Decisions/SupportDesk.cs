using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Integration.Examples.Decisions;

// A support desk triages each incoming message. A decider (in production, a model that returns
// typed decisions) reads the parsed ticket and picks a track. Refunds move money, so they need more
// confidence than the other tracks; anything the decider is unsure about goes to a person.

public sealed record SupportRequest(string CustomerId, string Message, decimal? OrderTotal);

public sealed record Ticket(string CustomerId, string Message, decimal? OrderTotal);

public sealed record RefundIssued(Ticket Ticket, decimal Amount);

/// <summary>What a track did. Every track produces one, so the chain after the switch can rely on it.</summary>
public sealed record TicketOutcome(IReadOnlyList<string> Actions);

public sealed record Resolution(
    string CustomerId,
    string Track,
    bool TookFallback,
    IReadOnlyList<string> Actions
);

[Asks("Which team should handle this support ticket?")]
public enum TicketTrack
{
    [System.ComponentModel.Description("The customer wants their money back for an order.")]
    Refund,

    [System.ComponentModel.Description(
        "An outage, a security problem or a legal threat. Page whoever is on call."
    )]
    Escalate,

    [System.ComponentModel.Description("A how-to question the help centre already answers.")]
    SelfServe,
}

public class TriageTicket(IDecider decider) : Train<SupportRequest, Resolution>
{
    protected override Task<Either<Exception, Resolution>> Junctions() =>
        AddServices(decider)
            .Chain<ParseTicket>()
            .Switch<Ticket, TicketTrack>(tracks =>
                tracks
                    .When(
                        TicketTrack.Refund,
                        t => t.Chain<IssueRefund>().Chain<NotifyCustomer>(),
                        requireConfidence: 0.7
                    )
                    .When(TicketTrack.Escalate, t => t.Chain<OpenIncident>())
                    .When(TicketTrack.SelfServe, t => t.Chain<SendHelpArticle>())
                    .RequireConfidence(0.5)
                    .Otherwise(t => t.Chain<QueueForHuman>())
            )
            .Chain<CloseTicket>()
            .Resolve();
}

public class ParseTicket : Junction<SupportRequest, Ticket>
{
    public override Task<Ticket> Run(SupportRequest input) =>
        Task.FromResult(new Ticket(input.CustomerId, input.Message.Trim(), input.OrderTotal));
}

public class IssueRefund : Junction<Ticket, RefundIssued>
{
    public override Task<RefundIssued> Run(Ticket input) =>
        Task.FromResult(new RefundIssued(input, input.OrderTotal ?? 0m));
}

public class NotifyCustomer : Junction<RefundIssued, TicketOutcome>
{
    public override Task<TicketOutcome> Run(RefundIssued input) =>
        Task.FromResult(
            new TicketOutcome([
                $"refunded {input.Amount:0.00}",
                $"emailed {input.Ticket.CustomerId}",
            ])
        );
}

public class OpenIncident : Junction<Ticket, TicketOutcome>
{
    public override Task<TicketOutcome> Run(Ticket input) =>
        Task.FromResult(new TicketOutcome(["opened incident", "paged on-call"]));
}

public class SendHelpArticle : Junction<Ticket, TicketOutcome>
{
    public override Task<TicketOutcome> Run(Ticket input) =>
        Task.FromResult(new TicketOutcome(["sent help article"]));
}

public class QueueForHuman : Junction<Ticket, TicketOutcome>
{
    public override Task<TicketOutcome> Run(Ticket input) =>
        Task.FromResult(new TicketOutcome(["queued for a person"]));
}

public class CloseTicket : Junction<(Ticket, TicketOutcome, TrackTaken<TicketTrack>), Resolution>
{
    public override Task<Resolution> Run((Ticket, TicketOutcome, TrackTaken<TicketTrack>) input)
    {
        var (ticket, outcome, taken) = input;

        return Task.FromResult(
            new Resolution(ticket.CustomerId, taken.Track, taken.TookFallback, outcome.Actions)
        );
    }
}

/// <summary>
/// A deterministic stand-in for a typed decision model. It scores each offered option by how many
/// of its keywords the message contains and turns the scores into probabilities, so it can be
/// unsure the way a model can, and reports confidence the way the shared request format does.
/// </summary>
public sealed class KeywordTriageDecider : IDecider
{
    private static readonly Dictionary<string, string[]> Keywords = new()
    {
        [nameof(TicketTrack.Refund)] = ["refund", "money back", "charged twice", "return"],
        [nameof(TicketTrack.Escalate)] = ["down", "outage", "breach", "lawyer", "hacked"],
        [nameof(TicketTrack.SelfServe)] = ["how do i", "where is", "reset my password"],
    };

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        var message = ((Ticket)request.State).Message.ToLowerInvariant();
        var question = (ChoiceQuestion)request.Questions.Single();

        // One point of prior on every option, so a message matching nothing is evenly unsure.
        var scores = question.Options.ToDictionary(
            o => o.Name,
            o => 1.0 + Keywords[o.Name].Count(message.Contains) * 4
        );
        var total = scores.Values.Sum();
        var probabilities = scores.ToDictionary(kv => kv.Key, kv => kv.Value / total);
        var best = probabilities.MaxBy(kv => kv.Value);
        var n = probabilities.Count;

        Answer answer = new ChoiceAnswer(best.Key, (n * best.Value - 1) / (n - 1), probabilities)
        {
            Model = "keyword-triage-1.0",
        };

        return Task.FromResult(
            new DecisionResult(new Dictionary<string, Answer> { [question.Key] = answer })
        );
    }
}
