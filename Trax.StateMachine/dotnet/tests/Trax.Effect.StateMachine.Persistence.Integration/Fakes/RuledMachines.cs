using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fakes;

// Machines that declare who may fire each event, start an instance and read one. Every one of them builds, so
// the assembly scan every InstanceHost runs can register them; a machine whose declaration a host must refuse is
// refused at startup, when it is served, never when it is built. None is marked [TraxMachine], for the same reason;
// the ones a test serves are left open, and the test registers their marked subclass (Fixtures/Marked) on its host.

/// <summary>The roles and the policy the ruled machines name.</summary>
public static class DeskRoles
{
    public const string Operator = "Operator";
    public const string Admin = "Admin";

    /// <summary>The policy <see cref="RuledDeskMachine"/>'s <c>PolicyOnly</c> event requires.</summary>
    public const string Policy = "desk-policy";

    /// <summary>
    /// A client acting for <paramref name="userKey"/> with exactly <paramref name="roles"/>, which satisfies
    /// <see cref="Policy"/> when it holds <see cref="Admin"/>.
    /// </summary>
    public static Actor.Client Client(string userKey, params string[] roles) =>
        new(
            userKey,
            role => roles.Contains(role, StringComparer.Ordinal),
            (policy, _) =>
                Task.FromResult(policy == Policy && roles.Contains(Admin, StringComparer.Ordinal))
        );
}

public enum DeskState
{
    Draft,
    Open,
    Done,
}

public enum DeskTrigger
{
    Open,
    OperatorOnly,
    OperatorOrAdmin,
    PolicyOnly,
    Anyone,
    Reopen,
}

public sealed record DeskContext
{
    [MinLength(1)]
    public string Title { get; init; } = "";
}

public sealed record DeskStart
{
    public string Title { get; init; } = "";
}

/// <summary>
/// A desk whose events each declare a different rule: from <c>Open</c>, <c>OperatorOnly</c> requires Operator,
/// <c>OperatorOrAdmin</c> requires either, <c>PolicyOnly</c> requires <see cref="DeskRoles.Policy"/> and
/// <c>Anyone</c> any authenticated caller, each to <c>Done</c>; <c>Reopen</c> (Operator) goes back to <c>Open</c>.
/// Starting one requires Operator, and only its owner reads it.
/// </summary>
public sealed class RuledDeskMachine : Machine<DeskState, DeskTrigger>
{
    public const string Id = "ruled-desk";

    protected override void Configure(IMachineBuilder<DeskState, DeskTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(DeskState.Draft, () => new JsonObject { ["title"] = "desk" })
            .Start<DeskStart>(s =>
                s.Reduce(Set((DeskContext c) => c.Title).FromInput((DeskStart i) => i.Title))
                    .Requires(DeskRoles.Operator)
            )
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(DeskState.Draft)
            .Context<DeskContext>()
            .On(DeskTrigger.Open)
            .AnyAuthenticated()
            .To(DeskState.Open);

        m.In(DeskState.Open)
            .Context<DeskContext>()
            .On(DeskTrigger.OperatorOnly)
            .Requires(DeskRoles.Operator)
            .To(DeskState.Done)
            .On(DeskTrigger.OperatorOrAdmin)
            .Requires(DeskRoles.Operator, DeskRoles.Admin)
            .To(DeskState.Done)
            .On(DeskTrigger.PolicyOnly)
            .RequiresPolicy(DeskRoles.Policy)
            .To(DeskState.Done)
            .On(DeskTrigger.Anyone)
            .AnyAuthenticated()
            .To(DeskState.Done);

        m.In(DeskState.Done)
            .Context<DeskContext>()
            .On(DeskTrigger.Reopen)
            .Requires(DeskRoles.Operator)
            .To(DeskState.Open);
    }

    /// <summary>A desk in <paramref name="state"/>, for seeding through the store.</summary>
    public static Snapshot In(DeskState state) =>
        new()
        {
            Machine = Id,
            Version = 1,
            State = state.ToString(),
            Context = new JsonObject { ["title"] = "desk" },
        };
}

public sealed record BuildStart
{
    public string Source { get; init; } = "";
}

/// <summary>
/// <see cref="BuildMachine"/> with its rules declared: entering <c>Building</c> (<c>Build</c>, <c>Retry</c>) requires
/// Operator, and editing or stopping needs only an authenticated caller.
/// </summary>
public sealed class RuledBuildMachine : Machine<BuildState, BuildTrigger>
{
    public const string Id = "ruled-build";

    protected override void Configure(IMachineBuilder<BuildState, BuildTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(BuildState.Draft, () => new JsonObject { ["source"] = "repo" })
            .Start<BuildStart>(s =>
                s.Reduce(
                        Set((BuildMachine.SourceContext c) => c.Source)
                            .FromInput((BuildStart i) => i.Source)
                    )
                    .AnyAuthenticated()
            )
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(BuildState.Draft)
            .Context<BuildMachine.SourceContext>()
            .On(BuildTrigger.Build)
            .Requires(DeskRoles.Operator)
            .To(BuildState.Building)
            .On(BuildTrigger.Edit)
            .AnyAuthenticated()
            .To(BuildState.Draft);

        m.In(BuildState.Building)
            .Context<BuildMachine.SourceContext>()
            .Invokes<IBuildTrain, BuildInput, BuildOutput>(ctx => new BuildInput(
                ctx["source"]!.GetValue<string>()
            ))
            .OnDone(
                BuildState.Built,
                reduce: Set((BuildMachine.BuiltContext c) => c.Artifact)
                    .FromInput((BuildOutput o) => o.Artifact)
            )
            .OnFailed(BuildState.BuildFailed)
            .OnCancelled(BuildState.BuildCancelled)
            .On(BuildTrigger.StopBuild)
            .AnyAuthenticated()
            .To(BuildState.Draft);

        m.In(BuildState.Built).Context<BuildMachine.BuiltContext>();

        m.In(BuildState.BuildFailed)
            .Context<BuildMachine.SourceContext>()
            .On(BuildTrigger.Retry)
            .Requires(DeskRoles.Operator)
            .To(BuildState.Building)
            .On(BuildTrigger.Edit)
            .AnyAuthenticated()
            .To(BuildState.Draft);

        m.In(BuildState.BuildCancelled)
            .Context<BuildMachine.SourceContext>()
            .On(BuildTrigger.Retry)
            .Requires(DeskRoles.Operator)
            .To(BuildState.Building);
    }

    /// <summary>A build in <paramref name="state"/> with the source context, for seeding through the store.</summary>
    public static Snapshot In(BuildState state) =>
        new()
        {
            Machine = Id,
            Version = 1,
            State = state.ToString(),
            Context = new JsonObject { ["source"] = "repo" },
        };
}

/// <summary>
/// A shipment whose one effect-bound event, <c>Ship</c>, requires Operator: <c>Packing -Ship-&gt; Shipped</c>, running
/// <see cref="IOrderCharge"/> exactly once.
/// </summary>
public sealed class RuledShipMachine : Machine<ShipState, ShipTrigger>
{
    public const string Id = "ruled-ship";

    protected override void Configure(IMachineBuilder<ShipState, ShipTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(ShipState.Packing, () => new JsonObject())
            .Start<DeskStart>(s => s.Reduce(Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(ShipState.Packing)
            .On(ShipTrigger.Ship)
            .Requires(DeskRoles.Operator)
            .RunsOnce<IOrderCharge>($"{Id}:ship")
            .Reduce((_, input) => new JsonObject { ["receipt"] = input?["receipt"]?.DeepClone() })
            .To(ShipState.Shipped);
    }

    public static Snapshot Packing =>
        new()
        {
            Machine = Id,
            Version = 1,
            State = nameof(ShipState.Packing),
            Context = new JsonObject(),
        };
}

public enum ListState
{
    Drafting,
    Finished,
}

public enum ListTrigger
{
    Finish,
}

public sealed record ListContext
{
    [MinLength(1)]
    public string Title { get; init; } = "";

    public int Copies { get; init; }
}

/// <summary>What a client starts a note with. Both fields are required.</summary>
public sealed record ListStart(string Title, int Copies);

/// <summary>
/// A note any authenticated caller starts from a <see cref="ListStart"/>: its title becomes the context's.
/// <c>Drafting -Finish-&gt; Finished</c>.
/// </summary>
public sealed class StartableMachine : Machine<ListState, ListTrigger>
{
    public const string Id = "startable";

    protected override void Configure(IMachineBuilder<ListState, ListTrigger> m) => Declare(m, Id);

    internal static void Declare(IMachineBuilder<ListState, ListTrigger> m, string id)
    {
        m.Id(id)
            .Version(1)
            .StartsAt(
                ListState.Drafting,
                () => new JsonObject { ["title"] = "untitled", ["copies"] = 1 }
            )
            .Start<ListStart>(s =>
                s.Reduce(Set((ListContext c) => c.Title).FromInput((ListStart i) => i.Title))
                    .AnyAuthenticated()
            )
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(ListState.Drafting)
            .Context<ListContext>()
            .On(ListTrigger.Finish)
            .AnyAuthenticated()
            .To(ListState.Finished);

        m.In(ListState.Finished).Context<ListContext>();
    }
}

/// <summary><see cref="StartableMachine"/> under another id, so one request id can start one of each.</summary>
public sealed class StartableTwinMachine : Machine<ListState, ListTrigger>
{
    public const string Id = "startable-twin";

    protected override void Configure(IMachineBuilder<ListState, ListTrigger> m) =>
        StartableMachine.Declare(m, Id);
}

/// <summary>
/// A build that starts in its invoking state, so a start queues the run, with at most one live run per owner.
/// </summary>
public sealed class InvokingStartMachine : Machine<BuildState, BuildTrigger>
{
    public const string Id = "invoking-start";

    protected override void Configure(IMachineBuilder<BuildState, BuildTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(BuildState.Building, () => new JsonObject { ["source"] = "repo" })
            .InvokedRunLimit(1)
            .Start<BuildStart>(s =>
                s.Reduce(
                        Set((BuildMachine.SourceContext c) => c.Source)
                            .FromInput((BuildStart i) => i.Source)
                    )
                    .AnyAuthenticated()
            )
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(BuildState.Building)
            .Context<BuildMachine.SourceContext>()
            .Invokes<IBuildTrain, BuildInput, BuildOutput>(ctx => new BuildInput(
                ctx["source"]!.GetValue<string>()
            ))
            .OnDone(
                BuildState.Built,
                reduce: Set((BuildMachine.BuiltContext c) => c.Artifact)
                    .FromInput((BuildOutput o) => o.Artifact)
            )
            .OnFailed(BuildState.BuildFailed)
            .OnCancelled(BuildState.BuildCancelled);

        m.In(BuildState.Built).Context<BuildMachine.BuiltContext>();
        m.In(BuildState.BuildFailed).Context<BuildMachine.SourceContext>();
        m.In(BuildState.BuildCancelled).Context<BuildMachine.SourceContext>();
    }
}

public enum ApprovalState
{
    Pending,
    Approved,
}

public enum ApprovalTrigger
{
    Approve,
}

public sealed record ApprovalContext
{
    public string ApprovedBy { get; init; } = "";
}

public sealed record ApprovalStart
{
    public string ApprovedBy { get; init; } = "";
}

/// <summary>
/// An approval only an operator gives: <c>Pending -Approve(Operator)-&gt; Approved</c>, with an owner-only discard.
/// <c>Approved</c> records a decision the server's gate let through.
/// </summary>
public sealed class OperatorApprovalMachine : Machine<ApprovalState, ApprovalTrigger>
{
    public const string Id = "operator-approval";

    protected override void Configure(IMachineBuilder<ApprovalState, ApprovalTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(ApprovalState.Pending, () => new JsonObject { ["approvedBy"] = "" })
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(ApprovalState.Pending)
            .Context<ApprovalContext>()
            .On(ApprovalTrigger.Approve)
            .Requires(DeskRoles.Operator)
            .Reduce(Set((ApprovalContext c) => c.ApprovedBy).ToValue("operator"))
            .To(ApprovalState.Approved);

        m.In(ApprovalState.Approved).Context<ApprovalContext>();
    }
}

/// <summary>
/// A start that reaches a field only a gated event writes: <c>Approve</c> (Operator) sets <c>approvedBy</c>, and the
/// start input sets it too, which refuses the build.
/// </summary>
public class ForgedStartMachine : Machine<ApprovalState, ApprovalTrigger>
{
    public const string Id = "forged-start";

    protected override void Configure(IMachineBuilder<ApprovalState, ApprovalTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(ApprovalState.Pending, () => new JsonObject { ["approvedBy"] = "" })
            .Start<ApprovalStart>(s =>
                s.Reduce(
                        Set((ApprovalContext c) => c.ApprovedBy)
                            .FromInput((ApprovalStart i) => i.ApprovedBy)
                    )
                    .AnyAuthenticated()
            )
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(ApprovalState.Pending)
            .Context<ApprovalContext>()
            .On(ApprovalTrigger.Approve)
            .Requires(DeskRoles.Operator)
            .Reduce(Set((ApprovalContext c) => c.ApprovedBy).ToValue("operator"))
            .To(ApprovalState.Approved);

        m.In(ApprovalState.Approved).Context<ApprovalContext>();
    }
}

/// <summary>
/// As <see cref="ForgedStartMachine"/>, but <c>Approve</c> requires a policy rather than a role: its write is still
/// one only the server makes, so the start must not reach it either.
/// </summary>
public sealed class PolicyWriteStartMachine : Machine<ApprovalState, ApprovalTrigger>
{
    public const string Id = "policy-write-start";

    protected override void Configure(IMachineBuilder<ApprovalState, ApprovalTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(ApprovalState.Pending, () => new JsonObject { ["approvedBy"] = "" })
            .Start<ApprovalStart>(s =>
                s.Reduce(
                        Set((ApprovalContext c) => c.ApprovedBy)
                            .FromInput((ApprovalStart i) => i.ApprovedBy)
                    )
                    .AnyAuthenticated()
            )
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(ApprovalState.Pending)
            .Context<ApprovalContext>()
            .On(ApprovalTrigger.Approve)
            .RequiresPolicy(DeskRoles.Policy)
            .Reduce(Set((ApprovalContext c) => c.ApprovedBy).ToValue("approver"))
            .To(ApprovalState.Approved);

        m.In(ApprovalState.Approved).Context<ApprovalContext>();
    }
}

/// <summary>
/// The declarative turnstile with each event's rule and the reads supplied by the caller, so a startup test can
/// declare exactly the gap it is about.
/// </summary>
internal static class RuledTurnstile
{
    internal delegate ITransitionBuilder<TurnstileState, TurnstileTrigger> EventRule(
        ITransitionBuilder<TurnstileState, TurnstileTrigger> edge
    );

    internal static ITransitionBuilder<TurnstileState, TurnstileTrigger> Undeclared(
        ITransitionBuilder<TurnstileState, TurnstileTrigger> edge
    ) => edge;

    internal static ITransitionBuilder<TurnstileState, TurnstileTrigger> AnyAuthenticated(
        ITransitionBuilder<TurnstileState, TurnstileTrigger> edge
    ) => edge.AnyAuthenticated();

    internal static void Declare(
        IMachineBuilder<TurnstileState, TurnstileTrigger> m,
        string id,
        EventRule coin,
        EventRule push,
        bool declareReads = true,
        Action<IReadRuleBuilder>? get = null,
        Action<IReadRuleBuilder>? discard = null,
        bool declareDiscard = true,
        Action<IReadRuleBuilder>? watch = null
    )
    {
        m.Id(id).Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());
        if (declareReads)
            m.Start<DeskStart>(s => s.Reduce(Keep()).AnyAuthenticated())
                .Get(get ?? (r => r.OwnerOnly()))
                .Watch(watch ?? get ?? (r => r.OwnerOnly()));
        if (declareReads && declareDiscard)
            m.Discard(discard ?? (r => r.OwnerOnly()));

        coin(m.In(TurnstileState.Locked).Context().On(TurnstileTrigger.Coin))
            .WithInput<DeclarativeTurnstileMachine.CoinInput>()
            .When(
                Input((DeclarativeTurnstileMachine.CoinInput i) => i.Coin)
                    .IsOneOf("quarter", "dollar")
            )
            .Because("Only a quarter or a dollar is accepted.")
            .Reduce(
                Set((DeclarativeTurnstileMachine.UnlockedContext u) => u.PaidWith)
                    .FromInput((DeclarativeTurnstileMachine.CoinInput i) => i.Coin)
            )
            .To(TurnstileState.Unlocked);

        push(
                m.In(TurnstileState.Unlocked)
                    .Context<DeclarativeTurnstileMachine.UnlockedContext>()
                    .On(TurnstileTrigger.Push)
            )
            .Reduce(Clear())
            .To(TurnstileState.Locked);
    }
}

/// <summary>
/// Declares every rule, and is left open so a test can mark a subclass <c>[TraxMachine]</c> in an assembly it builds:
/// the attribute on a type in this assembly would serve it in every host that scans it.
/// </summary>
public class MarkableRuledMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "markable-ruled";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated
        );
}

/// <summary>Leaves <c>Push</c>'s rule out, and is open for the same reason as <see cref="MarkableRuledMachine"/>.</summary>
public class MarkablePartlyRuledMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "markable-partly-ruled";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(m, Id, RuledTurnstile.AnyAuthenticated, RuledTurnstile.Undeclared);
}

/// <summary>
/// Declares every rule and is system-owned, and is open for the same reason as <see cref="MarkableRuledMachine"/>.
/// </summary>
public class MarkableSystemOwnedMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "markable-system-owned";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated
        );
        m.SystemOwned();
    }
}

/// <summary>Declares <c>Coin</c>'s rule and leaves <c>Push</c>'s out.</summary>
public class PartlyRuledMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "partly-ruled";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(m, Id, RuledTurnstile.AnyAuthenticated, RuledTurnstile.Undeclared);
}

/// <summary><c>Coin</c> requires a policy no host registers.</summary>
public class PolicyRuledMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "policy-ruled";
    public const string MissingPolicy = "no-such-desk-policy";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            e => e.RequiresPolicy(MissingPolicy),
            RuledTurnstile.AnyAuthenticated
        );
}

/// <summary>The owner reads an instance only while holding <see cref="Viewer"/>.</summary>
public sealed class ViewerReadMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "viewer-read";
    public const string Viewer = "Viewer";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            get: r => r.OwnerOnly().Requires(Viewer)
        );
}

/// <summary>The owner discards an instance only while holding <see cref="DeskRoles.Operator"/>.</summary>
public sealed class OperatorDiscardMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "operator-discard";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            discard: r => r.OwnerOnly().Requires(DeskRoles.Operator)
        );
}

/// <summary>Declares every rule but <c>discard</c>'s.</summary>
public class UndeclaredDiscardMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "undeclared-discard";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            declareDiscard: false
        );
}

/// <summary>
/// The owner reads an instance only while holding <see cref="ViewerReadMachine.Viewer"/>, but watches it without,
/// which would stream what <c>get</c> withholds.
/// </summary>
public class LooseWatchMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "loose-watch";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            get: r => r.OwnerOnly().Requires(ViewerReadMachine.Viewer),
            watch: r => r.OwnerOnly()
        );
}

/// <summary>Watches only with the role <c>get</c> needs, and one more: stricter than get, which is allowed.</summary>
public class StrictWatchMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "strict-watch";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            get: r => r.OwnerOnly().Requires(ViewerReadMachine.Viewer, DeskRoles.Operator),
            watch: r =>
                r.OwnerOnly().Requires(ViewerReadMachine.Viewer).Requires(DeskRoles.Operator)
        );
}

/// <summary>Lets any authenticated caller discard, looser than owner-only.</summary>
public class LooseDiscardMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "loose-discard";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            discard: r => r.AnyAuthenticated()
        );
}

/// <summary>A user-owned machine whose events admit anonymous callers.</summary>
public class AnonymousUserMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "anonymous-user";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(m, Id, e => e.Anonymous(), e => e.Anonymous());
}

/// <summary>Every event declares a rule; <c>start</c>, <c>get</c> and the push (<c>&lt;machine&gt;Changed</c>) declare none.</summary>
public class UndeclaredReadsMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "undeclared-reads";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            declareReads: false
        );
}

/// <summary>Lets anyone, signed in or not, read an instance through <c>get</c>.</summary>
public class LooseReadMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "loose-read";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m) =>
        RuledTurnstile.Declare(
            m,
            Id,
            RuledTurnstile.AnyAuthenticated,
            RuledTurnstile.AnyAuthenticated,
            get: r => r.Anonymous()
        );
}

/// <summary>
/// Fully declared, with <c>Coin</c> requiring Operator, and system-owned: only code creates and drives one.
/// </summary>
public class RuledSystemTurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "ruled-system-turnstile";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        RuledTurnstile.Declare(
            m,
            Id,
            e => e.Requires(DeskRoles.Operator),
            RuledTurnstile.AnyAuthenticated
        );
        m.SystemOwned();
    }
}

/// <summary>The raw-delegate turnstile with every rule declared: it has no exportable IR.</summary>
public class RuledRawTurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "ruled-raw-turnstile";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(TurnstileState.Locked, () => new JsonObject())
            .Start<DeskStart>(s => s.Reduce(Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(TurnstileState.Locked)
            .On(TurnstileTrigger.Coin)
            .AnyAuthenticated()
            .When(
                (_, input) =>
                    input is JsonObject o
                    && o["coin"]?.GetValueKind() == JsonValueKind.String
                    && o["coin"]!.GetValue<string>() is "quarter" or "dollar"
            )
            .Reduce((_, input) => new JsonObject { ["paidWith"] = input!["coin"]!.DeepClone() })
            .To(TurnstileState.Unlocked);

        m.In(TurnstileState.Unlocked)
            .On(TurnstileTrigger.Push)
            .AnyAuthenticated()
            .Reduce((_, _) => new JsonObject())
            .To(TurnstileState.Locked);
    }
}
