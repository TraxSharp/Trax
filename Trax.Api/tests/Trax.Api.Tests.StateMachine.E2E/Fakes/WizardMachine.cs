using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

/// <summary>
/// A declarative wizard shaped like Recovery's topic map, carrying every kind of trigger the typed machine surface
/// has to tell apart:
/// <code>
/// start --> Naming --Describe--> Paying --Pay (effect, Payer)--> Approving --Approve (Approver)--> Building
///             ▲        ◀──Back──                                   │ after 1 hour                 │ done / failed / cancelled
///             │                                                    └──────────> Expired           ▼
///             └───────────────────────Edit──────────────────── Built, BuildFailed, BuildCancelled, Expired, Held
///                                                     Building ◀──Rebuild (Approver)── Built, BuildFailed, BuildCancelled
///                                                     Building --CancelBuild--> Held --Approve (Approver)--> Building
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// Every client event declares its rule, <c>start</c>, <c>get</c> and the push (<c>&lt;machine&gt;Changed</c>) declare theirs, and the
/// start takes its own <see cref="WizardStartInput"/>. The outcome triggers of <see cref="WizardState.Building"/>,
/// the timer of <see cref="WizardState.Approving"/> and the effect's receipt are server-only, so none of them may
/// appear as a client argument.
/// </para>
/// <para>
/// Registered by every host built over this assembly, and served to clients only where a host registers its marked
/// subclass, <see cref="ServedWizardMachine"/>, in its place; the hosts of the generic mutation tests leave it unserved.
/// </para>
/// </remarks>
public class WizardMachine : Machine<WizardState, WizardTrigger>
{
    public const string MachineId = "wizard";

    /// <summary>The roles the rules name, which the E2E API keys carry.</summary>
    public const string PayerRole = "Payer";

    public const string ApproverRole = "Approver";

    /// <summary>The message of the <see cref="WizardTrigger.Describe"/> guard.</summary>
    public const string NeedsPages = "A wizard needs at least one page.";

    /// <summary>How long <see cref="WizardState.Approving"/> is held before it expires.</summary>
    public static readonly TimeSpan ApprovalWindow = TimeSpan.FromHours(1);

    protected override void Configure(IMachineBuilder<WizardState, WizardTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(WizardState.Naming, () => new JsonObject());

        m.Start<WizardStartInput>(s =>
            s.Reduce(Set((WizardContext c) => c.Title).FromInput((WizardStartInput i) => i.Title))
                .AnyAuthenticated()
        );
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());

        ConfigureUpToApproval(m);
        ConfigureBuildAndAfter(m);
    }

    private static void ConfigureUpToApproval(IMachineBuilder<WizardState, WizardTrigger> m)
    {
        m.In(WizardState.Naming)
            .Context<WizardContext>()
            .On(WizardTrigger.Describe)
            .WithInput<WizardDetailsInput>()
            .AnyAuthenticated()
            .When(Input((WizardDetailsInput i) => i.Pages).GreaterThan(0))
            .Because(NeedsPages)
            .Reduce(
                SetAll(
                    Set((WizardContext c) => c.Title).FromInput((WizardDetailsInput i) => i.Title),
                    Set((WizardContext c) => c.Pages).FromInput((WizardDetailsInput i) => i.Pages),
                    Set((WizardContext c) => c.Note).FromInput((WizardDetailsInput i) => i.Note)
                )
            )
            .To(WizardState.Paying);

        m.In(WizardState.Paying)
            .Context<WizardContext>()
            .On(WizardTrigger.Back)
            .AnyAuthenticated()
            .To(WizardState.Naming)
            .On(WizardTrigger.Pay)
            .WithInput<WizardReceipt>()
            .Requires(PayerRole)
            .RunsOnce<IOrderCharge>("wizard:pay")
            .Reduce(Set((WizardContext c) => c.Receipt).FromInput((WizardReceipt r) => r.Receipt))
            .To(WizardState.Approving);

        m.In(WizardState.Approving)
            .Context<WizardContext>()
            .On(WizardTrigger.Approve)
            .Requires(ApproverRole)
            .To(WizardState.Building)
            .After(ApprovalWindow)
            .GoTo(WizardState.Expired);
    }

    private static void ConfigureBuildAndAfter(IMachineBuilder<WizardState, WizardTrigger> m)
    {
        m.In(WizardState.Building)
            .Context<WizardContext>()
            .Invokes<IBuildWizardTrain, BuildWizardInput, WizardArtifact>(BuildInput)
            .OnDone(
                WizardState.Built,
                reduce: Set((WizardContext c) => c.Artifact)
                    .FromInput((WizardArtifact o) => o.Artifact)
            )
            .OnFailed(WizardState.BuildFailed)
            .OnCancelled(WizardState.BuildCancelled)
            .On(WizardTrigger.CancelBuild)
            .AnyAuthenticated()
            .To(WizardState.Held);

        // Approving records the payment's receipt, so only Pay may enter it; a stopped build waits here.
        m.In(WizardState.Held)
            .Context<WizardContext>()
            .On(WizardTrigger.Approve)
            .Requires(ApproverRole)
            .To(WizardState.Building)
            .On(WizardTrigger.Edit)
            .AnyAuthenticated()
            .To(WizardState.Naming);

        foreach (
            var ended in new[]
            {
                WizardState.Built,
                WizardState.BuildFailed,
                WizardState.BuildCancelled,
            }
        )
            m.In(ended)
                .Context<WizardContext>()
                .On(WizardTrigger.Rebuild)
                .Requires(ApproverRole)
                .To(WizardState.Building)
                .On(WizardTrigger.Edit)
                .AnyAuthenticated()
                .To(WizardState.Naming);

        m.In(WizardState.Expired)
            .Context<WizardContext>()
            .On(WizardTrigger.Edit)
            .AnyAuthenticated()
            .To(WizardState.Naming);
    }

    /// <summary>The client events, by the field name each has under <c>machines.wizard</c>.</summary>
    public static readonly IReadOnlyList<string> ClientEventFields =
    [
        "describe",
        "back",
        "pay",
        "approve",
        "cancelBuild",
        "rebuild",
        "edit",
    ];

    // Built on the server from the context Building was entered with.
    private static BuildWizardInput BuildInput(JsonObject ctx) =>
        new()
        {
            Title = ctx["title"]?.GetValue<string>() ?? "",
            Pages = ctx["pages"]?.GetValue<int>() ?? 0,
        };

    // One reduction that sets several fields. Rules.Set sets one; the Set reduction holds a list of steps.
    private static Reduction SetAll(params Reduction[] sets) =>
        new Reduction.Set(sets.SelectMany(s => ((Reduction.Set)s).Steps).ToList());
}
