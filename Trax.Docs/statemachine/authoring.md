---
layout: default
title: Authoring a machine
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

Three calls. `AddMediator` scans your assembly plus `StateMachineMutations.Assembly` (the four generic
mutations ship there, not in your assembly, so Trax can route them by input type). `AddTraxStateMachines`
discovers every machine and wires the store, the effect-claim ledger, the exactly-once runner, and the
registry. The host binds only the two things a machine can't know: how to map its auth to a user key, and
each effect implementation.

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString).AddJson())
        .AddMediator(typeof(CheckoutMachine).Assembly, StateMachineMutations.Assembly));

builder.Services.AddTraxStateMachines(typeof(CheckoutMachine).Assembly);

builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
builder.Services.AddScoped<ICharge, StripeCharge>();

builder.Services.AddDbContext<SnapshotDbContext>(o => o.UseNpgsql(connectionString));
```

To expire abandoned drafts, use the `configure` overload with a `DraftTtl`. A load of a draft idle past the
window discards it and the user starts fresh, so a forgotten form (or a finished one) never lingers. The
default is off.

```csharp
builder.Services.AddTraxStateMachines(
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
An unauthenticated caller gets the opaque authorization error at HTTP 200, not a crash.

A complete, runnable version of this (two machines, a GraphQL host, exactly-once over the wire) lives in the
`StateMachine` sample under `Trax.Samples`.

## Keep the two runtimes in parity

If your machine has a TypeScript twin, add a `differential` block to its `machine.json` so the exhaustive
[differential corpus](/docs/statemachine#two-runtimes-one-behavior) can enumerate it. Two fields, both small:

- `samples`: a few representative inputs per trigger (a no-input case is always added). A guard that accepts
  `quarter`/`dollar` wants `[{"coin":"quarter"},{"coin":"penny"},{}]` — one that passes, one that fails, one malformed.
- `seeds`: a representative valid context for any state a trigger cannot reach (context that arrives via
  autosave rather than a transition). The initial state and everything reachable from it need no seed.

```json
"differential": {
  "samples": { "Pay": [{ "receipt": "rcpt_1" }, {}] },
  "seeds": { "Review": { "items": ["book"], "total": 5, "receipt": null } }
}
```

Regenerate the corpus with `UPDATE_DIFFERENTIAL=1 npm test`; commit the resulting `differential.json`. Both
engines then replay it and fail loudly if their hand-written guards or reducers ever drift apart.

## SDK Reference

> [AddTraxStateMachines](/docs/sdk-reference/statemachine-api/add-trax-state-machines) | [Machine authoring](/docs/sdk-reference/statemachine-api/fluent-authoring) | [AddMediator](/docs/sdk-reference/mediator-api/add-service-train-bus)
