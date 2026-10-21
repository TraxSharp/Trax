using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Machines;
using Trax.Samples.Board.Trains.Work;

namespace Trax.Samples.Board.Tickets;

/// <summary>A lane of the board: the state it works, the role that works it, and what a claim hands its worker.</summary>
public sealed record LaneSpec(
    string Key,
    TicketState State,
    string Role,
    string Instructions,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> Events
);

public static class Lanes
{
    public static readonly IReadOnlyList<LaneSpec> All =
    [
        new(
            "triage",
            TicketState.Triage,
            BoardRoles.Triage,
            "Name the area that owns the report.",
            ["board"],
            [nameof(AssignmentTrigger.Triage)]
        ),
        new(
            "reproduce",
            TicketState.Reproducing,
            BoardRoles.Reproducer,
            "Send a subtotal and the shipping the policy says it pays, where the rules charge something else. Close the ticket after two cases that do not reproduce it.",
            ["charge"],
            [nameof(AssignmentTrigger.SubmitRepro), nameof(AssignmentTrigger.Close)]
        ),
        new(
            "fix",
            TicketState.ReadyToFix,
            BoardRoles.Fixer,
            "Send a patch to the free-shipping threshold. The repro case and every earlier case are run on it.",
            ["charge"],
            [nameof(AssignmentTrigger.SubmitFix)]
        ),
        new(
            "qa",
            TicketState.InQA,
            BoardRoles.Qa,
            "Send subtotals to run on the patched rules, around the threshold.",
            ["charge"],
            [nameof(AssignmentTrigger.SubmitChecks)]
        ),
        new(
            "review",
            TicketState.InReview,
            BoardRoles.Reviewer,
            "Approve the fix, or send it back with a reason.",
            ["board"],
            [nameof(AssignmentTrigger.Approve), nameof(AssignmentTrigger.SendBack)]
        ),
    ];

    public static LaneSpec? Find(string key) => All.FirstOrDefault(l => l.Key == key);

    public static TicketView View(Ticket ticket) =>
        new()
        {
            Id = ticket.Id,
            Title = ticket.Report.Title,
            Report = ticket.Report.Text,
            State = ticket.State.ToString(),
            Lane = ticket.Lane ?? "done",
            ClaimedBy = ticket.Context.ClaimedBy is { } holder ? TicketBoard.Name(holder) : null,
            Note = ticket.Context.Note,
            By = ticket.Context.By,
        };
}
