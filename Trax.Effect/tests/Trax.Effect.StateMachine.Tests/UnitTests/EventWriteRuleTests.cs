using System.Text.Json.Nodes;
using AwesomeAssertions;
using Ingest.Contracts;
using Trax.Effect.StateMachine.Tests.Fakes;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// A field the server writes, by an event gated by a role or a policy, an effect's receipt or an invoked train's
/// outcome, is decided by the server, as it is for the start: an event any caller may send copies no such field from
/// its input, or the machine is not built. Writing a constant there, or a field no server write writes, is fine.
/// </summary>
[Property("adr", Adr)]
public class EventWriteRuleTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    public enum DocState
    {
        Draft,
        Approved,
    }

    public enum DocTrigger
    {
        Edit,
        Approve,
    }

    public sealed record DocContext
    {
        public string Title { get; init; } = "";
        public string ApprovedBy { get; init; } = "";
    }

    public sealed record EditInput
    {
        public string Title { get; init; } = "";
        public string ApprovedBy { get; init; } = "";
    }

    private static MachineBuilder<DocState, DocTrigger> Machine(
        Func<
            ITransitionBuilder<DocState, DocTrigger>,
            ITransitionBuilder<DocState, DocTrigger>
        > edit,
        Func<
            ITransitionBuilder<DocState, DocTrigger>,
            ITransitionBuilder<DocState, DocTrigger>
        > approve
    )
    {
        var m = new MachineBuilder<DocState, DocTrigger>();
        m.Id("doc")
            .Version(1)
            .StartsAt(DocState.Draft, () => new JsonObject { ["title"] = "", ["approvedBy"] = "" });
        var draft = m.In(DocState.Draft).Context<DocContext>();
        edit(draft.On(DocTrigger.Edit).WithInput<EditInput>()).To(DocState.Draft);
        approve(draft.On(DocTrigger.Approve)).To(DocState.Approved);
        m.In(DocState.Approved).Context<DocContext>();
        return m;
    }

    private static ITransitionBuilder<DocState, DocTrigger> ApprovedByOperator(
        ITransitionBuilder<DocState, DocTrigger> t
    ) => t.Requires("Operator").Reduce(Set((DocContext c) => c.ApprovedBy).ToValue("operator"));

    private static Reduction Both(Reduction first, Reduction second) =>
        new Reduction.Set([.. ((Reduction.Set)first).Steps, .. ((Reduction.Set)second).Steps]);

    [Test]
    public void An_open_event_writing_a_field_a_gated_event_writes_refuses_to_build()
    {
        var m = Machine(
            edit =>
                edit.AnyAuthenticated()
                    .Reduce(
                        Both(
                            Set((DocContext c) => c.Title).FromInput((EditInput i) => i.Title),
                            Set((DocContext c) => c.ApprovedBy)
                                .FromInput((EditInput i) => i.ApprovedBy)
                        )
                    ),
            ApprovedByOperator
        );

        m.Invoking(b => b.Build())
            .Should()
            .Throw<InvalidOperationException>(
                $"a field a gated event writes is the server's, as it is for the start ({Adr})"
            )
            .Which.Message.Should()
            .Contain("Edit")
            .And.Contain("Approve")
            .And.Contain("'approvedBy'")
            .And.NotContain("'title'");
    }

    [Test]
    public void An_event_with_no_rule_writing_a_field_a_policy_gated_event_writes_refuses_to_build()
    {
        var m = Machine(
            edit =>
                edit.Reduce(
                    Set((DocContext c) => c.ApprovedBy).FromInput((EditInput i) => i.ApprovedBy)
                ),
            approve =>
                approve
                    .RequiresPolicy("approvers")
                    .Reduce(Set((DocContext c) => c.ApprovedBy).ToValue("operator"))
        );

        m.Invoking(b => b.Build())
            .Should()
            .Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("'approvedBy'");
    }

    [Test]
    public void An_open_event_with_a_custom_reducer_and_a_gated_write_refuses_to_build()
    {
        var m = Machine(
            edit => edit.AnyAuthenticated().Reduce((ctx, input) => ctx),
            ApprovedByOperator
        );

        m.Invoking(b => b.Build())
            .Should()
            .Throw<InvalidOperationException>("an opaque reducer may copy any field from its input")
            .Which.Message.Should()
            .Contain("Edit");
    }

    [Test]
    public void An_open_event_writing_only_other_fields_or_constants_builds()
    {
        var m = Machine(
            edit =>
                edit.AnyAuthenticated()
                    .Reduce(
                        Both(
                            Set((DocContext c) => c.Title).FromInput((EditInput i) => i.Title),
                            Set((DocContext c) => c.ApprovedBy).ToValue("")
                        )
                    ),
            ApprovedByOperator
        );

        m.Invoking(b => b.Build()).Should().NotThrow();
    }

    public enum PayState
    {
        Review,
        Paid,
    }

    public enum PayTrigger
    {
        Pay,
        Amend,
    }

    public sealed record PayContext
    {
        public string Receipt { get; init; } = "";
        public string Note { get; init; } = "";
    }

    public sealed record ReceiptInput
    {
        public string Receipt { get; init; } = "";
    }

    public sealed record AmendInput
    {
        public string Receipt { get; init; } = "";
        public string Note { get; init; } = "";
    }

    public interface IPayEffect;

    [Test]
    public void An_open_event_copying_a_field_an_effect_writes_refuses_to_build()
    {
        var m = new MachineBuilder<PayState, PayTrigger>();
        m.Id("pay")
            .Version(1)
            .StartsAt(PayState.Review, () => new JsonObject { ["receipt"] = "", ["note"] = "" });
        m.In(PayState.Review)
            .Context<PayContext>()
            .On(PayTrigger.Pay)
            .AnyAuthenticated()
            .RunsOnce<IPayEffect>()
            .WithInput<ReceiptInput>()
            .Reduce(Set((PayContext c) => c.Receipt).FromInput((ReceiptInput i) => i.Receipt))
            .To(PayState.Paid);
        m.In(PayState.Paid)
            .Context<PayContext>()
            .On(PayTrigger.Amend)
            .AnyAuthenticated()
            .WithInput<AmendInput>()
            .Reduce(Set((PayContext c) => c.Receipt).FromInput((AmendInput i) => i.Receipt))
            .To(PayState.Paid);

        m.Invoking(b => b.Build())
            .Should()
            .Throw<InvalidOperationException>(
                $"an effect's receipt is the server's, as a gated event's write is ({Adr})"
            )
            .Which.Message.Should()
            .Contain("Amend")
            .And.Contain("'receipt'");
    }

    [Test]
    public void An_open_event_copying_a_field_an_invoked_runs_outcome_writes_refuses_to_build()
    {
        var m = new MachineBuilder<IngestState, IngestTrigger>();
        m.Id("ingest").StartsAt(IngestState.Idle, () => new JsonObject { ["fingerprint"] = "" });
        m.In(IngestState.Idle).On(IngestTrigger.Start).To(IngestState.Fetching);
        m.In(IngestState.Fetching)
            .Invokes<IFetchTrain, FetchInput, FetchOutput>(_ => new FetchInput("s"))
            .OnDone(
                IngestState.Fetched,
                reduce: Set((FetchedContext c) => c.Fingerprint)
                    .FromInput((FetchOutput o) => o.Fingerprint)
            )
            .OnFailed(IngestState.FetchFailed)
            .OnCancelled(IngestState.Cancelled);
        m.In(IngestState.Fetched)
            .Context<FetchedContext>()
            .On(IngestTrigger.Approve)
            .AnyAuthenticated()
            .WithInput<FetchedContext>()
            .Reduce(
                Set((FetchedContext c) => c.Fingerprint)
                    .FromInput((FetchedContext i) => i.Fingerprint)
            )
            .To(IngestState.Approved);
        m.In(IngestState.FetchFailed);
        m.In(IngestState.Cancelled);
        m.In(IngestState.Approved).Context<FetchedContext>();

        m.Invoking(b => b.Build())
            .Should()
            .Throw<InvalidOperationException>(
                $"an invoked run's outcome is the server's, as a gated event's write is ({Adr})"
            )
            .Which.Message.Should()
            .Contain("Approve")
            .And.Contain("'fingerprint'");
    }

    [Test]
    public void An_open_event_into_a_state_that_requires_the_field_absent_builds()
    {
        var m = new MachineBuilder<PayState, PayTrigger>();
        m.Id("pay")
            .Version(1)
            .StartsAt(PayState.Review, () => new JsonObject { ["receipt"] = "", ["note"] = "" });
        m.In(PayState.Review)
            .Context<PayContext>()
            .Requires(Field((PayContext c) => c.Receipt).Absent())
            .On(PayTrigger.Amend)
            .AnyAuthenticated()
            .WithInput<AmendInput>()
            .Reduce((ctx, _) => ctx)
            .To(PayState.Review)
            .On(PayTrigger.Pay)
            .AnyAuthenticated()
            .RunsOnce<IPayEffect>()
            .WithInput<ReceiptInput>()
            .Reduce(Set((PayContext c) => c.Receipt).FromInput((ReceiptInput i) => i.Receipt))
            .To(PayState.Paid);
        m.In(PayState.Paid).Context<PayContext>();

        m.Invoking(b => b.Build())
            .Should()
            .NotThrow(
                $"the state's own invariant refuses a receipt there, whatever the reducer writes ({Adr})"
            );
    }

    public sealed record FetchedContext
    {
        public string Fingerprint { get; init; } = "";
    }
}
