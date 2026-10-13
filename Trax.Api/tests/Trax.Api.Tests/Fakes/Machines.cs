using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

// The machines of the typed machine surface's schema tests. Each is marked [TraxMachine], so it is served wherever it
// is registered; no host scans this assembly, and a test registers only the machines it names (MachineSchemaHost), so
// a machine that refuses startup once served (a raw-delegate one, a name that collides) refuses only the host of the
// test about it. Every machine here builds.

public enum ShopState
{
    Browsing,
    Ordered,
    Approved,
    Expired,
}

public enum ShopTrigger
{
    Submit,
    Ship,
    Approve,
    Reopen,
    Restart,
}

public sealed record ShopContext
{
    public string? Item { get; init; }

    public int? Quantity { get; init; }

    public string? Street { get; init; }
}

public sealed record ShopStartInput
{
    public string Item { get; init; } = "";
}

public sealed record ShopOrderInput
{
    public string Item { get; init; } = "";

    public int Quantity { get; init; }
}

/// <summary>The record two machines' events share, which the schema registers once.</summary>
public sealed record ShipToInput
{
    public string Street { get; init; } = "";
}

/// <summary>
/// A declarative shop with a timer: <c>Ordered</c> expires after an hour. Two events enter <c>Ordered</c>,
/// <c>Submit</c> (any authenticated caller) and <c>Reopen</c> (Clerk), so the timer leaving it carries the union of
/// their rules.
/// </summary>
[TraxMachine]
public sealed class ShopMachine : Machine<ShopState, ShopTrigger>
{
    public const string MachineId = "shop";

    /// <summary>The client events, by the field name each has under <c>machines.shop</c>.</summary>
    public static readonly IReadOnlyList<string> ClientEventFields =
    [
        "submit",
        "ship",
        "approve",
        "reopen",
        "restart",
    ];

    protected override void Configure(IMachineBuilder<ShopState, ShopTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(ShopState.Browsing, () => new JsonObject());
        m.Start<ShopStartInput>(s =>
            s.Reduce(Set((ShopContext c) => c.Item).FromInput((ShopStartInput i) => i.Item))
                .AnyAuthenticated()
        );
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());

        m.In(ShopState.Browsing)
            .Context<ShopContext>()
            .On(ShopTrigger.Submit)
            .WithInput<ShopOrderInput>()
            .AnyAuthenticated()
            .Reduce(Set((ShopContext c) => c.Quantity).FromInput((ShopOrderInput i) => i.Quantity))
            .To(ShopState.Ordered);

        m.In(ShopState.Ordered)
            .Context<ShopContext>()
            .On(ShopTrigger.Ship)
            .WithInput<ShipToInput>()
            .AnyAuthenticated()
            .Reduce(Set((ShopContext c) => c.Street).FromInput((ShipToInput i) => i.Street))
            .To(ShopState.Ordered)
            .On(ShopTrigger.Approve)
            .Requires("Approver")
            .To(ShopState.Approved)
            .After(TimeSpan.FromHours(1))
            .GoTo(ShopState.Expired);

        m.In(ShopState.Expired)
            .Context<ShopContext>()
            .On(ShopTrigger.Reopen)
            .Requires("Clerk")
            .To(ShopState.Ordered);

        m.In(ShopState.Approved)
            .Context<ShopContext>()
            .On(ShopTrigger.Restart)
            .AnyAuthenticated()
            .To(ShopState.Browsing);
    }
}

public enum PartsState
{
    Picking,
    Shipped,
}

public enum PartsTrigger
{
    Ship,
    Lookup,
}

public sealed record PartsContext
{
    public string? Street { get; init; }

    public string? Sku { get; init; }
}

public sealed record PartsStartInput
{
    public string Sku { get; init; } = "";
}

/// <summary>The input of <see cref="PartsTrigger.Lookup"/>, which a GraphQL query train also takes.</summary>
public sealed record CatalogLookupInput
{
    public string Sku { get; init; } = "";
}

public sealed record CatalogLookupOutput
{
    public string Sku { get; init; } = "";
}

/// <summary>A second machine sharing <see cref="ShipToInput"/> with <see cref="ShopMachine"/>.</summary>
[TraxMachine]
public sealed class PartsMachine : Machine<PartsState, PartsTrigger>
{
    public const string MachineId = "parts";

    protected override void Configure(IMachineBuilder<PartsState, PartsTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(PartsState.Picking, () => new JsonObject());
        m.Start<PartsStartInput>(s =>
            s.Reduce(Set((PartsContext c) => c.Sku).FromInput((PartsStartInput i) => i.Sku))
                .AnyAuthenticated()
        );
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());

        m.In(PartsState.Picking)
            .Context<PartsContext>()
            .On(PartsTrigger.Lookup)
            .WithInput<CatalogLookupInput>()
            .AnyAuthenticated()
            .Reduce(Set((PartsContext c) => c.Sku).FromInput((CatalogLookupInput i) => i.Sku))
            .To(PartsState.Picking)
            .On(PartsTrigger.Ship)
            .WithInput<ShipToInput>()
            .AnyAuthenticated()
            .Reduce(Set((PartsContext c) => c.Street).FromInput((ShipToInput i) => i.Street))
            .To(PartsState.Shipped);
    }
}

public enum StepState
{
    First,
    Second,
}

public enum StepTrigger
{
    Next,
    Back,
}

/// <summary>
/// A minimal declarative machine whose id the subclass chooses, for the tests about what a machine's id becomes in
/// the schema.
/// </summary>
public abstract class NamedStepMachine(string id) : Machine<StepState, StepTrigger>
{
    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(id).Version(1).StartsAt(StepState.First, () => new JsonObject());
        m.Start<StepStartInput>(s => s.AnyAuthenticated());
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(StepState.First)
            .Context()
            .On(StepTrigger.Next)
            .AnyAuthenticated()
            .To(StepState.Second);
        m.In(StepState.Second)
            .Context()
            .On(StepTrigger.Back)
            .AnyAuthenticated()
            .To(StepState.First);
    }
}

public sealed record StepStartInput
{
    public string? Note { get; init; }
}

// A machine id is kebab-case, so the only ids that normalise to one camelCase field are those a hyphen before a
// digit separates: "map-2d" and "map2d" both become map2d. An id starting with a digit is refused by the builder.

/// <summary>Normalises to the field <c>map2d</c>.</summary>
[TraxMachine]
public sealed class Map2dHyphenMachine() : NamedStepMachine("map-2d");

/// <summary>Normalises to the field <c>map2d</c> as well.</summary>
[TraxMachine]
public sealed class Map2dMachine() : NamedStepMachine("map2d");

public enum ReservedState
{
    Open,
    Closed,
}

public enum StartNamedTrigger
{
    Start,
}

public enum DiscardNamedTrigger
{
    Discard,
}

public enum GetNamedTrigger
{
    Get,
}

/// <summary>
/// A machine with one event, whose name the subclass's trigger enum chooses: an event named like one of the
/// operations every machine has (<c>start</c>, <c>discard</c>, <c>get</c>) would collide with it.
/// </summary>
public abstract class ReservedNameMachine<TTrigger>(string id) : Machine<ReservedState, TTrigger>
    where TTrigger : struct, Enum
{
    protected override void Configure(IMachineBuilder<ReservedState, TTrigger> m)
    {
        m.Id(id).Version(1).StartsAt(ReservedState.Open, () => new JsonObject());
        m.Start<StepStartInput>(s => s.AnyAuthenticated());
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(ReservedState.Open)
            .Context()
            .On(Enum.GetValues<TTrigger>()[0])
            .AnyAuthenticated()
            .To(ReservedState.Closed);
    }
}

[TraxMachine]
public sealed class StartEventMachine() : ReservedNameMachine<StartNamedTrigger>("start-event");

[TraxMachine]
public sealed class DiscardEventMachine()
    : ReservedNameMachine<DiscardNamedTrigger>("discard-event");

[TraxMachine]
public sealed class GetEventMachine() : ReservedNameMachine<GetNamedTrigger>("get-event");

/// <summary>The machine's own CLR state enum, named so the generated state type cannot be its name.</summary>
public enum LedgerStage
{
    Drafted,
    Posted,
}

public enum LedgerTrigger
{
    Post,
}

/// <summary>
/// A CLR enum spelt like the state type the schema generates for <see cref="LedgerMachine"/>, as Recovery's
/// <c>TopicMapState</c> is for the topic map. A train's output exposes it.
/// </summary>
public enum LedgerState
{
    Open,
    Reconciled,
    Archived,
}

public sealed record LedgerSummaryInput
{
    public string Book { get; init; } = "";
}

public sealed record LedgerSummaryOutput
{
    public LedgerState State { get; init; }
}

[TraxMachine]
public sealed class LedgerMachine : Machine<LedgerStage, LedgerTrigger>
{
    public const string MachineId = "ledger";

    protected override void Configure(IMachineBuilder<LedgerStage, LedgerTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(LedgerStage.Drafted, () => new JsonObject());
        m.Start<StepStartInput>(s => s.AnyAuthenticated());
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(LedgerStage.Drafted)
            .Context()
            .On(LedgerTrigger.Post)
            .AnyAuthenticated()
            .To(LedgerStage.Posted);
    }
}

/// <summary>Raw delegates throughout: no IR, no input schema, nothing a typed surface can be built from.</summary>
[TraxMachine]
public sealed class RawDelegateMachine : Machine<StepState, StepTrigger>
{
    public const string MachineId = "raw-delegate";

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(StepState.First, () => new JsonObject());
        m.In(StepState.First)
            .Holds(_ => null)
            .On(StepTrigger.Next)
            .When((_, input) => input is JsonObject)
            .Reduce((ctx, _) => ctx)
            .To(StepState.Second);
        m.In(StepState.Second).Holds(_ => null).On(StepTrigger.Back).To(StepState.First);
    }
}

/// <summary>A declarative machine whose instances belong to the system: no client may reach one.</summary>
[TraxMachine]
public sealed class SystemOwnedStepMachine : Machine<StepState, StepTrigger>
{
    public const string MachineId = "system-owned-step";

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(StepState.First, () => new JsonObject()).SystemOwned();
        m.In(StepState.First).Context().On(StepTrigger.Next).To(StepState.Second);
        m.In(StepState.Second).Context().On(StepTrigger.Back).To(StepState.First);
    }
}

/// <summary>
/// Declares rules, but one event's guard is a delegate: the IR and its hash do not cover the lambda, so changing it
/// would never make a client outdated.
/// </summary>
[TraxMachine]
public sealed class MixedGuardMachine : Machine<StepState, StepTrigger>
{
    public const string MachineId = "mixed-guard";

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(StepState.First, () => new JsonObject());
        m.Start<StepStartInput>(s => s.AnyAuthenticated());
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(StepState.First)
            .Context()
            .On(StepTrigger.Next)
            .AnyAuthenticated()
            .When((ctx, _) => ctx.Count == 0)
            .Because("Only an empty context moves on.")
            .To(StepState.Second);
        m.In(StepState.Second)
            .Context()
            .On(StepTrigger.Back)
            .AnyAuthenticated()
            .To(StepState.First);
    }
}

/// <summary>A user-owned machine with an event any caller may fire, signed in or not.</summary>
[TraxMachine]
public sealed class AnonymousEventMachine : Machine<StepState, StepTrigger>
{
    public const string MachineId = "anonymous-event";

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(StepState.First, () => new JsonObject());
        m.Start<StepStartInput>(s => s.AnyAuthenticated());
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(StepState.First).Context().On(StepTrigger.Next).Anonymous().To(StepState.Second);
        m.In(StepState.Second)
            .Context()
            .On(StepTrigger.Back)
            .AnyAuthenticated()
            .To(StepState.First);
    }
}

/// <summary>Declares every event's rule and its start, but no rule for <c>get</c>.</summary>
[TraxMachine]
public sealed class NoReadRuleMachine : Machine<StepState, StepTrigger>
{
    public const string MachineId = "no-read-rule";

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(StepState.First, () => new JsonObject());
        m.Start<StepStartInput>(s => s.AnyAuthenticated());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(StepState.First)
            .Context()
            .On(StepTrigger.Next)
            .AnyAuthenticated()
            .To(StepState.Second);
        m.In(StepState.Second)
            .Context()
            .On(StepTrigger.Back)
            .AnyAuthenticated()
            .To(StepState.First);
    }
}

/// <summary>Declares its start without a rule for who may start one.</summary>
[TraxMachine]
public sealed class NoStartRuleMachine : Machine<StepState, StepTrigger>
{
    public const string MachineId = "no-start-rule";

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(StepState.First, () => new JsonObject());
        m.Start<StepStartInput>(_ => { });
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(StepState.First)
            .Context()
            .On(StepTrigger.Next)
            .AnyAuthenticated()
            .To(StepState.Second);
        m.In(StepState.Second)
            .Context()
            .On(StepTrigger.Back)
            .AnyAuthenticated()
            .To(StepState.First);
    }
}

/// <summary>Declares its reads and start, but its <c>Back</c> event names no rule.</summary>
[TraxMachine]
public sealed class UndeclaredRuleMachine : Machine<StepState, StepTrigger>
{
    public const string MachineId = "undeclared-rule";

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(StepState.First, () => new JsonObject());
        m.Start<StepStartInput>(s => s.AnyAuthenticated());
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(StepState.First)
            .Context()
            .On(StepTrigger.Next)
            .AnyAuthenticated()
            .To(StepState.Second);
        m.In(StepState.Second).Context().On(StepTrigger.Back).To(StepState.First);
    }
}
