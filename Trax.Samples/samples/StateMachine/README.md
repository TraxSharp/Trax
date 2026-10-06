# State Machine sample

A GraphQL host over two portable snapshot state machines, authored with the fluent API and driven through
the four generic `stateMachine` mutations. It shows the whole feature end to end: fluent authoring,
one-line discovery, server-side authority (the server, not the browser, decides what a valid draft is,
down to the checkout total), and an exactly-once effect.

![The StateMachine page: the checkout is Paid, a second Pay charged nothing, and the payment provider lists one charge](screenshot.png)

| Machine | Shape | Notes |
|---|---|---|
| `turnstile` | `Locked ⇄ Unlocked` | No effect, no committed state. The structure proof. |
| `checkout` | `Cart → Review → Paid` (v2) | `Paid` is committed; `Pay` runs one irreversible charge exactly once. v2 adds a `total` that every state requires to equal 999 cents per item, backfilled from v1 drafts by a forward migration (see below). |

Both live in `Trax.Samples.StateMachine` as `Machine<TState, TTrigger>` subclasses. The host
(`Trax.Samples.StateMachine.Api`) wires them with one line:

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString).AddJson())
        .AddStateMachines(typeof(TurnstileMachine).Assembly)   // before AddMediator
        .AddMediator(typeof(TurnstileMachine).Assembly));

builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
builder.Services.AddSingleton<SimulatedPaymentProvider>();
builder.Services.AddScoped<ICharge, LoggingCharge>();
```

`AddStateMachines` discovers the machines and wires the store, the effect-claim ledger, the
exactly-once runner, the registry and the four mutations. It must come before `AddMediator`. The host
supplies only the two things a machine can't know: how to map its auth to a user key
(`ISnapshotPrincipal`) and the charge implementation.

## Run it

From `Trax.Samples/`:

```bash
docker compose up -d                             # Postgres 5432, database trax_statemachine
dotnet run --project samples/StateMachine/Trax.Samples.StateMachine.Api
```

Open http://localhost:5280/trax/graphql and send `X-Api-Key: alice-key-do-not-use-in-production`.

```graphql
# What machines are available?
{ discover { stateMachine { listMachines { machines { name hasEffect } } } } }

# Save a checkout draft (use one UUID id throughout), then charge it exactly once.
mutation {
  dispatch { stateMachine { saveSnapshot(input: {
    machine: "checkout",
    id: "11111111-1111-1111-1111-111111111111",
    snapshot: "{\"machine\":\"checkout\",\"version\":2,\"state\":\"Review\",\"context\":{\"items\":[\"book\"],\"receipt\":null,\"total\":999}}"
  }) { output { snapshot problem { code } } } } }
}

mutation {
  dispatch { stateMachine { sendSnapshot(input: {
    machine: "checkout", id: "11111111-1111-1111-1111-111111111111", requestId: "pay-1"
  }) { output { snapshot problem { code } } } } }
}
```

The second `sendSnapshot` returns the same `Paid` snapshot and does not charge again. Save the same draft
with `"items":["book","pen"]` and `"total":1` and the answer is `problem { code: "invalid-context" }`:
the total is the amount a real `ICharge` would take, so the machine refuses a total that is not 999 cents
per item, and the charge reads the amount from the server's stored copy (`CheckoutMachine.AmountCents`).

## Schema evolution (v1 → v2)

`checkout` is version 2: its context is `{ items, receipt, total }`, and every state requires `total` to be a
number. A draft written by the old v1 host has no `total`, so it would fail v2 validation. The machine
declares a forward migration inline, so a v1 draft is upgraded on rehydrate:

```csharp
m.Id("checkout").Version(2).StartsAt(CheckoutState.Cart, Fresh)
    .MigrateFrom(1, (state, ctx) =>
    {
        var next = (JsonObject)ctx.DeepClone();
        next["total"] = ItemsCount(ctx) * UnitPriceCents; // backfill from the item count
        return new MigrationResult(state, next);
    });
```

Load a stored v1 draft and the server returns it at v2 with `total` filled in; there is no manual step. A v1
snapshot an old client saves is stored as v2 the same way. `ForwardMigrationTests` in the E2E suite proves both
over GraphQL, and that a v2 context missing `total` is refused, which is what makes the migration load-bearing.

## Web frontend

A React app in [web/](web/) drives both machines through these mutations with a machine-agnostic transport
and a `useMachine` hook. Start this host, then `cd web && npm install && npm run dev` and open
http://localhost:5173. The host allows that origin only, so Vite refuses to start rather than move to
another port while a different sample's client holds 5173.

The page teaches the machine before the example. Each machine has a tab: the turnstile first, as the simplest
machine, then the checkout, a machine with an effect. A state diagram follows every move and marks the moves out of
the current state, and every trigger can be sent from any state, so pressing one with no arrow shows the server's
`no-transition`. **What the server just did** lists the checks the server made for the last request, in the order
it makes them, and where it stopped (`guard-failed` for a penny, `invalid-context` for a $0.01 total). On the
checkout, **Side effect: the charge** shows the `RunsOnce<ICharge>` binding, the whole request the page sends to pay
(only the draft's id), and the payment provider's own list of charges, which grows by one on Pay and not at all on
a second Pay. **Show the raw requests** opens every GraphQL request the page sends.

The charge is the one place a payment happens: `LoggingCharge` takes it from `SimulatedPaymentProvider`, an in-memory
stand-in for a payment gateway, and `payments.listCharges` returns the caller's own charges for the page to show.

## Tests

```bash
dotnet test tests/Trax.Samples.StateMachine.Tests                         # the machines, no database
dotnet test tests/Trax.Samples.StateMachine.E2E                           # the real host over GraphQL
```

`Trax.Samples.StateMachine.E2E` starts this host with `WebApplicationFactory` against the
`statemachine_e2e_tests` database (`docker compose up -d` creates it on a fresh volume) and drives everything
above over `/trax/graphql`: both machines, the refused transitions, the charge running once however often
`sendSnapshot` is repeated, the v1 to v2 migration, the server-checked total, and auth (anonymous refused, the
demo keys only in Development). Set `TRAX_TEST_PG_PORT` when your Postgres is not on 5432. Without a database it
fails; it never skips.

## Notes

- Anonymous requests get a `TRAX_AUTHORIZATION` error at HTTP 200, not a crash. The four mutations carry
  `[TraxAuthorize]`; `listMachines` is anonymous so you can inspect the machines without a key.
- The `snapshot_draft` and `effect_claim` tables are created by the Trax Postgres provider's own
  migrations when the host starts; the sample writes no schema of its own.

Docs: <https://traxsharp.net/docs/samples/state-machine>
