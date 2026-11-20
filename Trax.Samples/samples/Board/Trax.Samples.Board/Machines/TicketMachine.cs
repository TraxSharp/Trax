using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

/// <summary>
/// The board. One instance per ticket, owned by the system: no client reads or moves it. Host code does, and only
/// two kinds: the claim train, and a check train once it has run an agent's evidence and accepted it. So the lanes a
/// ticket may move through, and what each move needs, are decided here and nowhere a client can reach.
/// <code>
/// Triage --Triaged--> Reproducing --Reproduced--> ReadyToFix --Fixed--> InQA --QaPassed--> InReview --Approved--> Done
///                     Reproducing --CannotReproduce (after 2 failed cases)--> Closed
///                     InQA --QaFailed--> ReadyToFix;  InReview --SentBack--> ReadyToFix
/// every lane: --Claim--> itself (once), --Release--> itself
/// </code>
/// </summary>
public sealed class TicketMachine : Machine<TicketState, TicketTrigger>
{
    public const string MachineId = "ticket";

    /// <summary>The state each lane works, by lane key.</summary>
    public static readonly IReadOnlyDictionary<string, TicketState> Lanes = new Dictionary<
        string,
        TicketState
    >(StringComparer.Ordinal)
    {
        ["triage"] = TicketState.Triage,
        ["reproduce"] = TicketState.Reproducing,
        ["fix"] = TicketState.ReadyToFix,
        ["qa"] = TicketState.InQA,
        ["review"] = TicketState.InReview,
    };

    private static readonly Reduction TakeClaim = SetAll(
        Set((TicketContext c) => c.ClaimedBy).FromInput((ClaimInput i) => i.By),
        Set((TicketContext c) => c.ClaimedAt).FromInput((ClaimInput i) => i.At)
    );

    private static readonly Reduction DropClaim = SetAll(
        Set((TicketContext c) => c.ClaimedBy).ToValue(null),
        Set((TicketContext c) => c.ClaimedAt).ToValue(null)
    );

    // Moving a ticket to another lane ends the claim on it and leaves a note.
    private static Reduction Moved(params Reduction[] more) =>
        SetAll([
            Set((TicketContext c) => c.ClaimedBy).ToValue(null),
            Set((TicketContext c) => c.ClaimedAt).ToValue(null),
            Set((TicketContext c) => c.Note).FromInput((NoteInput i) => i.Note),
            Set((TicketContext c) => c.By).FromInput((NoteInput i) => i.By),
            .. more,
        ]);

    public static MachineKey KeyFor(string ticketId) => MachineKey.Of(ticketId);

    public static Guid IdFor(string ticketId) => MachineInstanceId.For(MachineId, KeyFor(ticketId));

    public static JsonObject ContextFor(string ticketId, string report) =>
        new() { ["id"] = ticketId, ["report"] = report };

    protected override void Configure(IMachineBuilder<TicketState, TicketTrigger> m)
    {
        m.Id(MachineId)
            .Version(1)
            .SystemOwned()
            .StartsAt(TicketState.Triage, () => ContextFor("unknown", "unknown"));

        Lane(m, TicketState.Triage)
            .On(TicketTrigger.Triaged)
            .WithInput<TriagedInput>()
            .Reduce(Moved(Set((TicketContext c) => c.Area).FromInput((TriagedInput i) => i.Area)))
            .To(TicketState.Reproducing);

        Lane(m, TicketState.Reproducing)
            // The case did not show the bug: the claim stays, so the same worker can try another.
            .On(TicketTrigger.NotReproduced)
            .WithInput<CaseInput>()
            .Reduce(
                SetAll(
                    Set((TicketContext c) => c.FailedCases)
                        .FromInput((CaseInput i) => i.FailedCases),
                    Set((TicketContext c) => c.Note).FromInput((CaseInput i) => i.Note),
                    Set((TicketContext c) => c.By).FromInput((CaseInput i) => i.By)
                )
            )
            .To(TicketState.Reproducing)
            .On(TicketTrigger.Reproduced)
            .WithInput<CaseInput>()
            .Reduce(
                Moved(
                    Set((TicketContext c) => c.ReproSubtotal)
                        .FromInput((CaseInput i) => i.Subtotal),
                    Set((TicketContext c) => c.ReproCharged).FromInput((CaseInput i) => i.Charged)
                )
            )
            .To(TicketState.ReadyToFix)
            .On(TicketTrigger.CannotReproduce)
            .WithInput<NoteInput>()
            .When(Field((TicketContext c) => c.FailedCases).AtLeast(2))
            .Because("A ticket is closed only after at least two cases did not reproduce it.")
            .Reduce(Moved())
            .To(TicketState.Closed);

        Lane(m, TicketState.ReadyToFix)
            .On(TicketTrigger.FixRejected)
            .WithInput<NoteInput>()
            .Reduce(
                SetAll(
                    Set((TicketContext c) => c.Note).FromInput((NoteInput i) => i.Note),
                    Set((TicketContext c) => c.By).FromInput((NoteInput i) => i.By)
                )
            )
            .To(TicketState.ReadyToFix)
            .On(TicketTrigger.Fixed)
            .WithInput<PatchInput>()
            .Reduce(
                Moved(
                    Set((TicketContext c) => c.FixFreeFrom).FromInput((PatchInput i) => i.FreeFrom),
                    Set((TicketContext c) => c.FixInclusive)
                        .FromInput((PatchInput i) => i.Inclusive)
                )
            )
            .To(TicketState.InQA);

        Lane(m, TicketState.InQA)
            .On(TicketTrigger.QaPassed)
            .WithInput<NoteInput>()
            .Reduce(Moved())
            .To(TicketState.InReview)
            .On(TicketTrigger.QaFailed)
            .WithInput<NoteInput>()
            .Reduce(Moved())
            .To(TicketState.ReadyToFix);

        Lane(m, TicketState.InReview)
            .On(TicketTrigger.Approved)
            .WithInput<NoteInput>()
            .Reduce(Moved())
            .To(TicketState.Done)
            .On(TicketTrigger.SentBack)
            .WithInput<NoteInput>()
            .Reduce(Moved())
            .To(TicketState.ReadyToFix);

        m.In(TicketState.Done).Context<TicketContext>();
        m.In(TicketState.Closed).Context<TicketContext>();
    }

    // A lane: a worker claims the ticket once, and the claim is released when its lease runs out.
    private static IStateBuilder<TicketState, TicketTrigger> Lane(
        IMachineBuilder<TicketState, TicketTrigger> m,
        TicketState state
    ) =>
        m.In(state)
            .Context<TicketContext>()
            .On(TicketTrigger.Claim)
            .WithInput<ClaimInput>()
            .When(Field((TicketContext c) => c.ClaimedBy).Absent())
            .Because("Another worker holds this ticket.")
            .Reduce(TakeClaim)
            .To(state)
            .On(TicketTrigger.Release)
            .Reduce(DropClaim)
            .To(state);
}
