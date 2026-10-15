using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

public enum WizardState
{
    /// <summary>The wizard's first step: what to make and how many pages.</summary>
    Naming,

    /// <summary>The order is described and waits for its payment.</summary>
    Paying,

    /// <summary>Paid; an approver lets the build start. Held too long, it expires.</summary>
    Approving,

    /// <summary>The build runs: <see cref="IBuildWizardTrain"/>.</summary>
    Building,

    /// <summary>The build finished; the context points at what it made.</summary>
    Built,

    /// <summary>The build failed.</summary>
    BuildFailed,

    /// <summary>The build's run was cancelled.</summary>
    BuildCancelled,

    /// <summary>Nobody approved in time.</summary>
    Expired,

    /// <summary>The build was stopped by its owner; an approver may start it again.</summary>
    Held,
}

public enum WizardTrigger
{
    /// <summary>Describe what to make; carries <see cref="WizardDetailsInput"/>.</summary>
    Describe,

    /// <summary>Back from the payment to the description.</summary>
    Back,

    /// <summary>Pay: runs the irreversible <see cref="IOrderCharge"/> exactly once. Needs the Payer role.</summary>
    Pay,

    /// <summary>Approve the paid order, which starts the build. Needs the Approver role.</summary>
    Approve,

    /// <summary>Stop the running build.</summary>
    CancelBuild,

    /// <summary>Build again with the same choices: a new run. Needs the Approver role.</summary>
    Rebuild,

    /// <summary>Start over from the description.</summary>
    Edit,
}

/// <summary>The wizard's context, one shape in every state.</summary>
public sealed record WizardContext
{
    public string? Title { get; init; }

    public int? Pages { get; init; }

    public string? Note { get; init; }

    /// <summary>The receipt the payment effect returned.</summary>
    public string? Receipt { get; init; }

    /// <summary>What the build made: a pointer, never the thing.</summary>
    public string? Artifact { get; init; }
}

/// <summary>What a client starts a wizard with. Never the context type.</summary>
public sealed record WizardStartInput
{
    [MinLength(1)]
    public string Title { get; init; } = "";
}

/// <summary>The input of <see cref="WizardTrigger.Describe"/>.</summary>
public sealed record WizardDetailsInput
{
    [MinLength(1)]
    public string Title { get; init; } = "";

    public int Pages { get; init; }

    /// <summary>Named differently on the wire, so validation must read it under its JSON name.</summary>
    [JsonPropertyName("note_text")]
    [MaxLength(10)]
    public string? Note { get; init; }

    /// <summary>Never recorded anywhere a run's input is copied.</summary>
    [TraxSensitive]
    public string? Secret { get; init; }
}

/// <summary>What the payment effect hands the <see cref="WizardTrigger.Pay"/> edge: never a client argument.</summary>
public sealed record WizardReceipt
{
    public string Receipt { get; init; } = "";
}

/// <summary>The input the server builds for <see cref="IBuildWizardTrain"/> from the context.</summary>
public sealed record BuildWizardInput
{
    public string Title { get; init; } = "";

    public int Pages { get; init; }
}

/// <summary>What <see cref="IBuildWizardTrain"/> returns.</summary>
public sealed record WizardArtifact
{
    public string Artifact { get; init; } = "";
}

public interface IBuildWizardTrain : IServiceTrain<BuildWizardInput, WizardArtifact>;

/// <summary>The train <see cref="WizardState.Building"/> invokes: effect junctions only, so a cancel stops it.</summary>
public class BuildWizardTrain : ServiceTrain<BuildWizardInput, WizardArtifact>, IBuildWizardTrain
{
    protected override Task<Either<Exception, WizardArtifact>> Junctions() =>
        Chain<BuildWizardStep>().Resolve();
}

public sealed class BuildWizardStep : EffectJunction<BuildWizardInput, WizardArtifact>
{
    public override Task<WizardArtifact> Run(BuildWizardInput input) =>
        Task.FromResult(new WizardArtifact { Artifact = $"artifact:{input.Title}:{input.Pages}" });
}

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
