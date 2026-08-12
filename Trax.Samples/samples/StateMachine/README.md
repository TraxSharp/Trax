# State Machine sample

A GraphQL host over two portable snapshot state machines, authored with the fluent API and driven through
the four generic `stateMachine` mutations. It shows the whole feature end to end: fluent authoring,
one-line discovery, server-side authority, and an exactly-once effect.

| Machine | Shape | Notes |
|---|---|---|
| `turnstile` | `Locked ⇄ Unlocked` | No effect, no committed state. The structure proof. |
| `checkout` | `Cart → Review → Paid` (v2) | `Paid` is committed; `Pay` runs one irreversible charge exactly once. v2 adds a `total`, backfilled from v1 drafts by a forward migration (see below). |

Both live in `Trax.Samples.StateMachine` as `Machine<TState, TTrigger>` subclasses. The host
(`Trax.Samples.StateMachine.Api`) wires them with one line:

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(e => e.UsePostgres(cs).AddJson())
        .AddMediator(typeof(TurnstileMachine).Assembly, StateMachineMutations.Assembly));
builder.Services.AddTraxStateMachines(typeof(TurnstileMachine).Assembly);

builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
builder.Services.AddScoped<ICharge, LoggingCharge>();
```

`AddTraxStateMachines` discovers the machines and wires the store, the effect-claim ledger, the
exactly-once runner, and the registry. The host supplies only the two things a machine can't know: how to
map its auth to a user key (`ISnapshotPrincipal`) and the charge implementation.

## Run it

```bash
cd Trax.Samples && docker compose up -d          # Postgres
./pack-local.sh                                  # local Trax packages
dotnet run --project samples/StateMachine/Trax.Samples.StateMachine.Api
```

Open http://localhost:5220/trax/graphql and send `X-Api-Key: alice-key`.

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

The second `sendSnapshot` returns the same `Paid` snapshot and does not charge again.

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

Load a stored v1 draft and the server returns it at v2 with `total` filled in; there is no manual step. That
migration is guarded by tests in `Trax.Samples.StateMachine.Tests` (a v1 draft loads as v2, and a v2 context
missing `total` is rejected, which is what makes the migration load-bearing).

## Web frontend

A React app in [web/](web/) drives both machines through these mutations with a machine-agnostic transport
and a `useMachine` hook. Start this host, then `cd web && npm install && npm run dev` and open
http://localhost:5173.

## Notes

- Anonymous requests get a `TRAX_AUTHORIZATION` error at HTTP 200, not a crash. The four mutations carry
  `[TraxAuthorize]`; `listMachines` is anonymous so you can inspect the machines without a key.
- The host creates its database and the `snapshot_draft` + `effect_claim` tables on startup
  (see `SnapshotSchema`). A production host ships those as a migration instead.
