---
layout: default
title: State Machine
description: "The StateMachine sample: two fluent machines behind the generic stateMachine mutations, a forward migration, an exactly-once charge and a server-owned total."
parent: Samples & Deployment
nav_order: 8
---

# State Machine

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

`samples/StateMachine` in [Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples) is a GraphQL host
over two snapshot state machines, authored with the fluent API and driven through the four generic
`stateMachine` mutations, plus a React app (`web/`) that drives them from a browser.

| Machine | Shape | What it shows |
|---|---|---|
| `turnstile` | `Locked ⇄ Unlocked` | Structure only: guards, reducers, per-state invariants. No effect. |
| `checkout` | `Cart → Review → Paid`, version 2 | A committed state, one irreversible charge run exactly once, a forward migration from v1, and a total the server checks |

## What it proves

Each row is proved over GraphQL against the running host by `tests/Trax.Samples.StateMachine.E2E` (see
[Tests](#tests)).

| Feature | Code | Proved by |
|---|---|---|
| One-line discovery: `AddStateMachines(assembly)` before `AddMediator`; both machines driven through the four mutations | `Api/Program.cs` | `TurnstileTests`, `CheckoutTests` |
| Illegal transitions refused as data (`guard-failed`, `no-transition`, `invalid-context`), the stored draft unchanged | `Machines.cs` | `TurnstileTests`, `CheckoutTests` |
| An effect that runs once per intent, however often `sendSnapshot` is repeated, and only a send reaches `Paid` | `Machines.cs`, `RunsOnce<ICharge>`, `Payments.cs` | `CheckoutTests`, `PaymentTests` |
| A v1 draft upgraded to v2 by `MigrateFrom(1, ...)`, on load and on save | `Machines.cs` | `ForwardMigrationTests` |
| Server authority over what a client autosaves: a draft whose total disagrees with its items is refused, and the charge takes the stored draft's total | `Machines.cs`, `LoggingCharge` | `ServerOwnedTotalTests` |
| The two bindings a host supplies: `ISnapshotPrincipal` (whose draft) and the effect (`ICharge`); each caller sees only their own drafts | `Api/Program.cs`, `SnapshotPrincipal.cs` | `TurnstileTests` |
| Anonymous callers refused; the demo keys exist only in Development | `Api/Program.cs` | `AuthenticationTests` |

## Run

From the `Trax.Samples` root:

```bash
docker compose up -d       # Postgres on 5432, database trax_statemachine
dotnet run --project samples/StateMachine/Trax.Samples.StateMachine.Api
```

The host listens on <http://localhost:5280> in Development (`Properties/launchSettings.json`), the
only environment that registers the demo keys `alice-key-do-not-use-in-production` and
`bob-key-do-not-use-in-production`. The `snapshot_draft` and `effect_claim` tables come from the Trax
Postgres provider's own migrations; the sample writes no schema.

For the browser app, start the host, then:

```bash
cd samples/StateMachine/web
npm install
npm run dev                # http://localhost:5173
```

The page teaches the machine before the example. Each machine has a tab: the turnstile first, as the simplest
machine, then the checkout, a machine with an effect. A state diagram follows every move and marks the moves out of
the current state, and every trigger can be sent from any state, so pressing one with no arrow shows the server's
`no-transition`. **What the server just did** lists the checks the server made for the last request, in the order
it makes them, and where it stopped (`guard-failed` for a penny, `invalid-context` for a $0.01 total). On the
checkout, **Side effect: the charge** shows the `RunsOnce<ICharge>` binding, the whole request the page sends to pay
(only the draft's id), and the payment provider's own list of charges, which grows by one on Pay and not at all on
a second Pay. **Show the raw requests** opens every GraphQL request the page sends.

## Try it

Send `X-Api-Key: alice-key-do-not-use-in-production` with each request (Nitro at
<http://localhost:5280/trax/graphql>, or curl).

```graphql
# What machines are there? (anonymous)
{ discover { stateMachine { listMachines { machines { name hasEffect } } } } }

# Save a checkout draft at Review. The total must be 999 cents per item.
mutation {
  dispatch { stateMachine { saveSnapshot(input: {
    machine: "checkout",
    id: "11111111-1111-1111-1111-111111111111",
    snapshot: "{\"machine\":\"checkout\",\"version\":2,\"state\":\"Review\",\"context\":{\"items\":[\"book\"],\"receipt\":null,\"total\":999}}"
  }) { output { snapshot problem { code } } } } }
}

# Pay: runs the charge once and moves to Paid
mutation {
  dispatch { stateMachine { sendSnapshot(input: {
    machine: "checkout", id: "11111111-1111-1111-1111-111111111111", requestId: "pay-1"
  }) { output { snapshot problem { code } } } } }
}
```

The `sendSnapshot` answer is the `Paid` snapshot with a `receipt`, and the host logs
`Charged 999 cents for checkout Review -> receipt rcpt_...`. Send it again and the same snapshot comes
back with no second charge. Save a draft with `"items":["book","pen"]` and `"total":1` under another
id and the answer is `problem { code: "invalid-context" }`.

The turnstile shows a refused transition. Save it `Locked`, then try a penny and a quarter:

```graphql
mutation {
  dispatch { stateMachine { saveSnapshot(input: {
    machine: "turnstile", id: "33333333-3333-3333-3333-333333333333",
    snapshot: "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Locked\",\"context\":{}}"
  }) { output { snapshot problem { code } } } } }
}

mutation {
  dispatch { stateMachine { advanceSnapshot(input: {
    machine: "turnstile", id: "33333333-3333-3333-3333-333333333333",
    trigger: "Coin", input: "{\"coin\":\"penny\"}"
  }) { output { snapshot problem { code message } } } } }
}
```

The penny comes back as `problem { code: "guard-failed", message: "Only a quarter or a dollar is accepted." }`
and the draft stays `Locked`. The same mutation with `"quarter"` answers the `Unlocked` snapshot with
`"paidWith":"quarter"`.

To see the migration, save a version 1 checkout, which has no `total`:

```graphql
mutation {
  dispatch { stateMachine { saveSnapshot(input: {
    machine: "checkout", id: "44444444-4444-4444-4444-444444444444",
    snapshot: "{\"machine\":\"checkout\",\"version\":1,\"state\":\"Review\",\"context\":{\"items\":[\"book\",\"pen\"],\"receipt\":null}}"
  }) { output { snapshot problem { code } } } } }
}
```

The answer is the draft at version 2 with `"total":1998`, which is what is stored.

Without the `X-Api-Key` header, each of these mutations answers HTTP 200 with
`errors: [{ message: "Not authorized.", extensions: { code: "TRAX_AUTHORIZATION" } }]`; only `listMachines` is
anonymous.

## How it works

### Registration

```csharp
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString).AddJson())
        .AddStateMachines(typeof(TurnstileMachine).Assembly)   // before AddMediator
        .AddMediator(typeof(TurnstileMachine).Assembly)
);

builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
builder.Services.AddSingleton<SimulatedPaymentProvider>();
builder.Services.AddScoped<ICharge, LoggingCharge>();
builder.Services.AddTraxGraphQL(graphql => graphql);
```

`AddStateMachines` discovers every `Machine<TState, TTrigger>` in the assembly and contributes the four
mutations to the mediator scan, so it must come before `AddMediator`. `TraxCallerSnapshotPrincipal`
maps the authenticated Trax caller to the draft's owner, so Alice and Bob each see their own draft
for the same id. See [AddStateMachines](/docs/sdk-reference/statemachine-api/add-trax-state-machines).

`LoggingCharge` takes the payment from `SimulatedPaymentProvider`, an in-memory stand-in for a payment gateway, for
the draft's user and the stored draft's total. Nothing else calls the provider: the page has no charge endpoint, only
`payments.listCharges`, a `[TraxAuthorize]` query that returns the caller's own charges, which is how the page shows
the effect ran once.

### The server owns the total

`saveSnapshot` is the soft path: the client writes the whole snapshot, context included, and the
server validates and stores it. Anything an effect will act on must therefore be checked by the
state's `Holds`, or the client decides it. The checkout's `total` is the amount a real `ICharge`
would take, so every state holds it to the item price:

```csharp
private static bool TotalMatchesItems(JsonObject ctx) =>
    ctx["total"] is JsonValue total
    && total.GetValueKind() == JsonValueKind.Number
    && total.ToJsonString()
        == ((long)ItemsCount(ctx) * UnitPriceCents).ToString(CultureInfo.InvariantCulture);

m.In(CheckoutState.Review)
    .Holds(ctx =>
        ItemsCount(ctx) > 0 && ReceiptEmpty(ctx) && TotalMatchesItems(ctx)
            ? null
            : "Review: non-empty items, no receipt, total = 999 cents per item."
    )
    .On(CheckoutTrigger.Pay)
    .When((ctx, input) => ItemsCount(ctx) > 0 && Receipt(input) is not null)
    .RunsOnce<ICharge>("checkout:charge")
    .To(CheckoutState.Paid);
```

The charge then reads the amount from the snapshot it is handed, which is the server's stored copy,
already validated. See [State Machines: Persistence](/docs/statemachine#persistence-and-exactly-once-effects).

### The forward migration

Version 2 added `total`. A draft an older host stored at version 1 has none, so it would fail the v2
invariants; `MigrateFrom(1, ...)` backfills it from the item count whenever the server reads a version 1
snapshot: a stored draft on load, advance or send, and a snapshot an older client sends to `saveSnapshot`:

```csharp
m.Id("checkout").Version(2).StartsAt(CheckoutState.Cart, Fresh)
    .MigrateFrom(1, (state, ctx) =>
    {
        var next = (JsonObject)ctx.DeepClone();
        next["total"] = ItemsCount(ctx) * UnitPriceCents;
        return new MigrationResult(state, next);
    });
```

A snapshot newer than the server's version is refused as `version-mismatch`. See
[Migrations](/docs/sdk-reference/statemachine-api/migrations).

## Tests

```bash
dotnet test tests/Trax.Samples.StateMachine.Tests    # the machines over an in-memory store, no database
dotnet test tests/Trax.Samples.StateMachine.E2E      # the real host over GraphQL, needs Postgres
```

`Trax.Samples.StateMachine.Tests` drives both machines through the draft service over an in-memory store: the
v1 to v2 migration, autosave then advance, the canonical wire, and the total check.

`Trax.Samples.StateMachine.E2E` starts the real host with `WebApplicationFactory<Program>` against a Postgres
database named `statemachine_e2e_tests` (`Host=localhost;Port=5432;Username=trax;Password=trax123`; set
`TRAX_TEST_PG_PORT` when your Postgres listens elsewhere) and sends every request to `/trax/graphql`, as the web
client does. Without the database the suite fails; it never skips. Three techniques carry over to testing any
state machine host:

- **Observe the effect by replacing it.** The factory binds a recording `ICharge` in place of the sample's
  logging one, which is the binding a real host swaps for a payment gateway anyway. The recording charge reads
  the amount the same way, `CheckoutMachine.AmountCents(snapshot)`, so a test can count charges and check the
  amount:

  ```csharp
  using Microsoft.AspNetCore.Hosting;
  using Microsoft.AspNetCore.TestHost;
  using Microsoft.Extensions.DependencyInjection;

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
      builder.UseSetting("ConnectionStrings:TraxDatabase", connectionString);
      builder.UseEnvironment("Development");   // the demo keys exist only here
      builder.ConfigureTestServices(services => services.AddSingleton<ICharge>(charges));
  }
  ```

- **Seed what an older host stored through the store.** `ISnapshotStore` does not validate, so writing a version
  1 draft through it reproduces a row the old host left behind, which `loadSnapshot` then upgrades. Drafts are
  keyed by `ISnapshotPrincipal.CurrentUserKey`, which for the sample's `TraxCallerSnapshotPrincipal` is the
  scheme-qualified principal id, `TraxApiKey:alice` for the demo key:

  ```csharp
  using var scope = factory.Services.CreateScope();
  var store = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
  await store.Insert("TraxApiKey:alice", id, new Snapshot
  {
      Machine = "checkout",
      Version = 1,
      State = "Review",
      Context = new JsonObject { ["items"] = new JsonArray("book"), ["receipt"] = null },
  });
  ```

- **Isolate tests by id and data, not by database.** Every test uses a fresh draft id and puts an item no other
  test uses in its cart, so one host and one database serve the whole suite and each test sees only its own
  drafts and charges.

`AuthenticationTests` also starts the host in Production: no API key scheme is registered there, the demo key is
refused, and registering a `do-not-use-in-production` key makes the host refuse to start.

## SDK Reference

> [AddStateMachines](/docs/sdk-reference/statemachine-api/add-trax-state-machines) | [Fluent authoring](/docs/sdk-reference/statemachine-api/fluent-authoring) | [Effects](/docs/sdk-reference/statemachine-api/effects) | [Migrations](/docs/sdk-reference/statemachine-api/migrations) | [Persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports)
