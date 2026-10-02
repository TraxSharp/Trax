---
layout: default
title: Authoring a machine
description: How to author a state machine as a Machine subclass, wire it into a host, drive it over GraphQL, and keep the C# and TypeScript runtimes in parity.
parent: State Machines
nav_order: 1
---

# Authoring a machine

A machine is a class. You subclass `Machine<TState, TTrigger>`, declare its states, transitions, guards,
reducers, committed states, and its one irreversible effect inline, and a host discovers it with one line.
There is no per-machine registration and no effect wiring in the composition root.

## Declare the machine

`TState` and `TTrigger` are your own enums. Everything about a transition lives on the transition it belongs
to: the guard that admits it, the reducer that computes the next context, and the effect it fires.

```csharp
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine;

public sealed class CheckoutMachine : Machine<CheckoutState, CheckoutTrigger>
{
    protected override void Configure(IMachineBuilder<CheckoutState, CheckoutTrigger> m)
    {
        m.Id("checkout").Version(1).StartsAt(CheckoutState.Cart, Fresh);

        m.In(CheckoutState.Cart)
            .Holds(ctx => ItemsIsArray(ctx) && ReceiptEmpty(ctx) ? null : "Cart: items[] and no receipt.")
            .On(CheckoutTrigger.Next)
            .To(CheckoutState.Review);

        m.In(CheckoutState.Review)
            .On(CheckoutTrigger.Pay)
            .When((ctx, input) => ItemsCount(ctx) > 0 && Receipt(input) is not null)
            .Because("Checkout needs items and a receipt to be paid.")
            .RunsOnce<ICharge>("checkout:charge")
            .Reduce((ctx, input) => WithReceipt(ctx, Receipt(input)))
            .To(CheckoutState.Paid);

        m.In(CheckoutState.Paid).Committed();
    }
}
```

| Builder call | What it declares |
| --- | --- |
| `Id` / `Version` / `StartsAt` | the machine's stable name, its definition version, and the initial state plus a factory for its context |
| `In(state)` | opens a state to add its context rule and its outgoing transitions |
| `Holds(validator)` | the state's context rule: return `null` when valid, or a reason string. Enforced on the way in (rehydrate) and out (advance) |
| `On(trigger)` | starts a transition out of the current state |
| `When(guard)` / `Because(message)` | admits the transition only when the guard passes; the message is surfaced on a rejection |
| `Reduce(reducer)` | computes the next context. Return a fresh JSON object; never mutate the input |
| `RunsOnce<TEffect>(keyPrefix)` | binds an irreversible effect that fires exactly once when this transition is sent |
| `To(state)` | the destination |
| `Committed()` | marks a state a soft autosave must not overwrite (a completed order) |

Guards and reducers are named code, never serialized. The snapshot carries structure and data, never logic.

## Wire it into a host

One builder step. `AddStateMachines` discovers every machine and wires the store, the effect-claim ledger,
the exactly-once runner, and the registry, over the data context of the provider you configured in
`AddEffects`, and contributes the four generic mutation trains to the mediator scan (they
ship in the persistence package, not your assembly, so Trax can route them by input type). Call it before
`AddMediator`. The host binds only the two things a machine can't know: how to map its auth to a user key, and
each effect implementation.

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString).AddJson())
        .AddStateMachines(typeof(CheckoutMachine).Assembly)
        .AddMediator(typeof(CheckoutMachine).Assembly));

builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
builder.Services.AddScoped<ICharge, StripeCharge>();
```

To expire abandoned drafts, use the `configure` overload with a `DraftTtl`. A load of a draft idle past the
window discards it and the user starts fresh, so a forgotten form (or a finished one) never lingers. The
default is off.

```csharp
trax.AddStateMachines(
    o => o.DraftTtl = TimeSpan.FromDays(30),
    typeof(CheckoutMachine).Assembly);
```

`ISnapshotPrincipal` maps the current caller to the user key that scopes drafts. Binding it over Trax's own
`TraxCaller` is a one-liner:

```csharp
public sealed class TraxCallerSnapshotPrincipal(TraxCaller caller) : ISnapshotPrincipal
{
    public string? CurrentUserKey => caller.IsAuthenticated ? caller.Principal!.Id : null;
}
```

## Drive it over GraphQL

The four generic mutations serve every registered machine under the `stateMachine` namespace. The machine
is a runtime argument (the `machine` field), so there is no per-machine mutation to write.

| Mutation | Trust level |
| --- | --- |
| `saveSnapshot` | soft path: the client sends a whole snapshot, the server validates and stores it |
| `advanceSnapshot` | authoritative: the client sends a trigger, the server re-drives the stored draft |
| `loadSnapshot` | resume: read the caller's stored draft (a missing draft is normal, not an error) |
| `sendSnapshot` | run the machine's one irreversible effect, exactly once and state-gated |

```graphql
mutation {
  dispatch { stateMachine { sendSnapshot(input: {
    machine: "checkout", id: "…", requestId: "pay-1"
  }) { output { snapshot problem { code } } } } }
}
```

Every rejection comes back as a typed `problem` in the data, never a thrown error across the boundary: an
unknown machine is `unknown-machine`, an invalid snapshot is `invalid-context`, a stale write is `conflict`.
A snapshot, a trigger input, or an advanced snapshot larger than 64 KiB is `too-large`, and nothing is written.

A `requestId` makes a retry safe: the same id with the same trigger returns the current snapshot instead of
firing again, and the same id with a different trigger is refused as `request-id-reused`. Advance and send
share one id space; a send with no `requestId` uses `send:{id}`. The full rule is under
[persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports#how-a-request-id-is-matched).
An unauthenticated caller gets the opaque authorization error at HTTP 200, not a crash, and a request that
reaches a mutation with no user key (`ISnapshotPrincipal.CurrentUserKey` is null) is refused as
`unauthenticated`.

A complete, runnable version of this (two machines, a GraphQL host, exactly-once over the wire) lives in the
`StateMachine` sample under `Trax.Samples`.

## Keep the two runtimes in parity

A TypeScript twin is generated from the machine's [IR](/docs/sdk-reference/statemachine-api/ir-format), so it
needs a [declaratively authored](/docs/statemachine/declarative-authoring) machine; the delegate machine above
cannot be exported. The exhaustive [differential corpus](/docs/statemachine#two-runtimes-one-behavior) is
generated from the same IR. Its inputs are authored in C#, in `Configure`, with `.Differential(...)`:

- **Samples**: a few representative inputs per trigger (a no-input case is always added). A guard that accepts
  `quarter`/`dollar` wants one input that passes, one that fails, and an empty one.
- **Seeds**: a representative valid context for any state a trigger cannot reach (context that arrives via
  autosave rather than a transition). The initial state and everything reachable from it need no seed.

```csharp
m.Differential(d => d
    .Sample(CheckoutTrigger.Pay, new JsonObject { ["receipt"] = "rcpt_1" })
    .EmptySample(CheckoutTrigger.Pay)
    .Seed(CheckoutState.Review, new JsonObject
    {
        ["items"] = new JsonArray("book"),
        ["total"] = 5,
        ["receipt"] = null,
    }));
```

They are exported into the IR's `differential` block, and
[`trax machine generate --corpus-out`](/docs/reference/cli#trax-machine-generate) regenerates
`differential.json` from it; commit the result. There is no `machine.json` to edit: the CLI reads only the IR
it exports from your compiled machine. Both engines then replay the corpus and fail loudly if their guards or
reducers ever drift apart. The [IDifferentialBuilder](/docs/sdk-reference/statemachine-api/fluent-authoring#idifferentialbuilder)
reference lists the typed overloads and probe contexts.

## SDK Reference

> [AddStateMachines](/docs/sdk-reference/statemachine-api/add-trax-state-machines) | [Machine authoring](/docs/sdk-reference/statemachine-api/fluent-authoring) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)
