---
layout: default
title: State Machine
description: "The StateMachine sample: two fluent machines started and moved by their events, a forward migration, an exactly-once charge and a server-owned total."
parent: Samples & Deployment
nav_order: 8
---

# State Machine

> NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

`samples/StateMachine` in [Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples) is a GraphQL host
over two snapshot state machines, authored declaratively with the fluent API, plus a React app (`web/`) that drives
them from a browser. Both are marked `[TraxMachine]`, so each is
served as its own typed operations under the [machines root](/docs/sdk-reference/graphql-api/machines), and nothing
else: the generic `stateMachine` mutations are not served. A client creates an instance with `start`, which mints
its id from the machine's declared start, and moves it with one mutation per event, each taking its own input
type; the checkout's `pay` runs the charge on the server. Nothing the page sends is a snapshot.

| Machine | Shape | What it shows |
|---|---|---|
| `turnstile` | `Locked ⇄ Unlocked` | Structure only: a guard on the coin, a reduction, per-state context records. No effect. |
| `checkout` | `Cart → Review → Paid`, version 2, declarative | A declared start, a rule on every event and read, a committed state, one irreversible charge run exactly once, a forward migration from v1, and a total only the server computes |

## What it proves

Each row is proved over GraphQL against the running host by `tests/Trax.Samples.StateMachine.E2E` (see
[Tests](#tests)).

| Feature | Code | Proved by |
|---|---|---|
| One-line discovery: `AddStateMachines(assembly)` before `AddMediator`, both machines exposed and the generic mutations not served; an instance created with `start` under a server-minted id, and a retried start answered with the same one | `Api/Program.cs` | `GenericMutationsOptInTests`, `TurnstileTests`, `CheckoutTests` |
| Illegal transitions refused as data (`guard-failed`, `no-transition`, `invalid-context`), the stored draft unchanged | `Machines.cs` | `TurnstileTests`, `CheckoutTests` |
| An effect that runs once per intent, however often `pay` is repeated with one request id, and only `pay` reaches `Paid`, taking no input | `Machines.cs`, `RunsOnce<ICharge>`, `Payments.cs` | `CheckoutTests`, `PaymentTests` |
| A v1 draft upgraded to v2 by `MigrateFrom(1, ...)` when the server reads it | `Machines.cs` | `ForwardMigrationTests` |
| Server authority: no client saves a draft (the host serves no save), `AddItem` prices the cart on the server, a stored draft whose total disagrees with its items cannot be read, and the charge takes the stored draft's total | `Machines.cs`, `LoggingCharge` | `ServerOwnedTotalTests`, `CheckoutTotalAuthorityTests` |
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
`no-transition`. The page starts each machine once per user and keeps the id the server minted in the browser, so
a reload resumes the same instance. **What the server just did** lists the checks the server made for the last
request, in the order it makes them, and where it stopped (`guard-failed` for a penny, or for **Next** from an
empty cart). On the checkout, **Fill the cart** sends `AddItem` with an item's name only, and the server prices
the cart. **Side effect: the charge** shows the `RunsOnce<ICharge>` binding, the whole request the page sends to pay
(only the draft's id), and the payment provider's own list of charges, which grows by one on Pay and not at all on
a second Pay. **Show the raw requests** opens every GraphQL request the page sends.

## Try it

Send `X-Api-Key: alice-key-do-not-use-in-production` with each request (Nitro at
<http://localhost:5280/trax/graphql>, or curl).

```graphql
# What machines are there? (anonymous)
{ discover { stateMachine { listMachines { machines { name hasEffect } } } } }

# Start a checkout. The server mints its id and starts it as an empty cart. A retry with the same
# requestId answers with the same checkout rather than a second one.
mutation {
  machines { checkout { start(requestId: "start-1") {
    id revision state context { items total } problem { code } } } }
}
```

The answer carries the `id` the server minted; use it in place of `ID` below. Fill the cart with `addItem`, which
names the item and nothing else, then review it and pay:

```graphql
mutation {
  machines { checkout { addItem(id: "ID", input: { item: "book" }, requestId: "add-1") {
    revision state context { items total } problem { code } } } }
}

mutation {
  machines { checkout { next(id: "ID", requestId: "next-1") { state problem { code } } } }
}

# Pay: runs the charge once and moves to Paid
mutation {
  machines { checkout { pay(id: "ID", requestId: "pay-1") {
    state context { receipt total } problem { code } } } }
}
```

The `addItem` answer is the cart with `items: ["book"]` and `total: 999`: the server appends the item and computes
the total, and no request can set it. `next` from an empty cart is `guard-failed`. The `pay` answer is `Paid` with a
`receipt`, and the host logs `Charged 999 cents for checkout Review -> receipt rcpt_...`. Send it again with the same
`requestId` and the same answer comes back with no second charge. `pay` takes no input: the charge produces the
receipt the edge records, so a client cannot hand it one, and nothing else reaches `Paid`. The host serves no save,
so no client writes a checkout at all. `reset` takes a paid checkout back to an empty cart, and when you are done,
discard it:

```graphql
mutation {
  machines { checkout { discard(id: "ID", requestId: "discard-1") { discarded problem { code } } } }
}
```

The turnstile shows a refused transition. Start one, then try a penny and a quarter with its id:

```graphql
mutation {
  machines { turnstile { start(requestId: "turnstile-1") { id state problem { code } } } }
}

mutation {
  machines { turnstile { coin(id: "ID", input: { coin: "penny" }, requestId: "coin-1") {
    state context { paidWith } problem { code message } } } }
}
```

The penny comes back as `problem { code: "guard-failed", message: "Only a quarter or a dollar is accepted." }`
and the turnstile stays `Locked`. The same mutation with `"quarter"` and a new `requestId` answers `Unlocked` with
`paidWith: "quarter"`. `{ machines { turnstile { get(id: "ID") { state context { paidWith } } } } }` reads it back.

Without the `X-Api-Key` header, each of these operations answers HTTP 200 with
`errors: [{ message: "Not authorized.", extensions: { code: "TRAX_AUTHORIZATION" } }]`; only `listMachines` is
anonymous.

The demo keys are two users every visitor shares, and the server caps the drafts one user may hold (50 by
default). The host keeps the cap and sets `DraftTtl` to a day, so the checkouts and turnstiles a walkthrough or a
closed browser leaves behind expire long before a demo database fills it; discarding one frees its place at once.

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
        .AddStateMachines(                                      // before AddMediator
            o => o.DraftTtl = TimeSpan.FromDays(1),
            typeof(TurnstileMachine).Assembly
        )
        .AddMediator(typeof(TurnstileMachine).Assembly)
);

builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
builder.Services.AddSingleton<SimulatedPaymentProvider>();
builder.Services.AddScoped<ICharge, LoggingCharge>();
builder.Services.AddTraxGraphQL(graphql =>
    graphql.UsePersistedOperations(persisted =>
        persisted
            .SingleNode()
            .RequirePersisted(false)
            .LogNonPersistedRequests(true)
            .ExposeOperationsNamespace(false)
    )
);
```

`AddStateMachines` discovers every `Machine<TState, TTrigger>` in the assembly and contributes the trains every
machine operation runs through to the mediator scan, so it must come before `AddMediator`. Both machines are marked
`[TraxMachine]`, so clients reach each through its own typed operations; an unmarked machine, and the generic
mutations, are not served. Every event, the start and the reads declare who may send them, and the host refuses to
start a marked machine that leaves one undeclared. Serving a machine requires persisted operations, because
each machine's operations are served as persisted documents that carry its version; this demo logs the ad-hoc
queries it is sent rather than refusing them, and serves no document-management operations. `TraxCallerSnapshotPrincipal`
maps the authenticated Trax caller to the draft's owner, so Alice and Bob each see their own draft
for the same id. See [AddStateMachines](/docs/sdk-reference/statemachine-api/add-trax-state-machines).

`LoggingCharge` takes the payment from `SimulatedPaymentProvider`, an in-memory stand-in for a payment gateway, for
the draft's user and the stored draft's total. Nothing else calls the provider: the page has no charge endpoint, only
`payments.listCharges`, a `[TraxAuthorize]` query that returns the caller's own charges, which is how the page shows
the effect ran once.

### A declarative checkout

The checkout is authored with the declarative surface, so everything it does is data the IR exporter writes out:
each state's context record and rule, and each event's rule, input, guard and reduction. A client starts it with the
declared start, and every event and read declares who may use it:

```csharp
m.Id("checkout").Version(2).StartsAt(CheckoutState.Cart, Fresh)
    .Start<EmptyStart>(s => s.Reduce(Keep()).AnyAuthenticated())
    .Get(r => r.OwnerOnly())
    .Watch(r => r.OwnerOnly())
    .Discard(r => r.OwnerOnly())
    .CustomGuard(TotalMatchesItems, (ctx, _) => TotalIsThePriceOfTheItems(ctx))
    .CustomReducer(AddItemToCart, AddToCart);

m.In(CheckoutState.Cart)
    .Context<CheckoutContext>()
    .Requires(All(Field((CheckoutContext c) => c.Receipt).Absent(), PricedByTheServer))
    .On(CheckoutTrigger.AddItem).AnyAuthenticated()
    .WithInput<ItemInput>()
    .When(Input((ItemInput i) => i.Item).NonEmpty()).Because("Name the item to add.")
    .Reduce(new Reduction.Custom(AddItemToCart))
    .To(CheckoutState.Cart)
    .On(CheckoutTrigger.Next).AnyAuthenticated()
    .When(HasItems).Because("Add an item before reviewing.")
    .To(CheckoutState.Review);

m.In(CheckoutState.Review)
    .Context<CheckoutContext>()
    .Requires(All(HasItems, Field((CheckoutContext c) => c.Receipt).Absent(), PricedByTheServer))
    .On(CheckoutTrigger.Pay).AnyAuthenticated()
    .WithInput<PayInput>()
    .When(All(HasItems, Input((PayInput i) => i.Receipt).NonEmpty()))
    .RunsOnce<ICharge>("checkout:charge")
    .Reduce(Set((PaidContext c) => c.Receipt).FromInput((PayInput i) => i.Receipt))
    .To(CheckoutState.Paid);
```

`Pay` is effect-bound, so it is served as `pay(id, requestId)` with no input: the server runs the charge and then fires it with the
receipt the charge returned.

### The server owns the total

The checkout's `total` is the amount a real `ICharge` would take, so no client writes it. A save of a checkout is
refused outright, and `AddItem` takes only an item's name: its reduction appends the item and computes the total on
the server. Every state also requires the total to be the item price, so a stored draft whose total disagrees with
its items cannot be read. The rule vocabulary has no arithmetic, so both are custom, a named reduction and a named
rule, each with one C# handler, which the IR carries by name:

```csharp
private static bool TotalIsThePriceOfTheItems(JsonObject ctx) =>
    ctx["total"] is JsonValue total
    && total.GetValueKind() == JsonValueKind.Number
    && total.ToJsonString()
        == ((long)ItemsCount(ctx) * UnitPriceCents).ToString(CultureInfo.InvariantCulture);

private static JsonObject AddToCart(JsonObject ctx, JsonNode? input)
{
    var next = (JsonObject)ctx.DeepClone();
    var items = next["items"] as JsonArray ?? [];
    items.Add(input!["item"]!.GetValue<string>());
    next["items"] = items;
    next["total"] = (long)items.Count * UnitPriceCents;
    return next;
}
```

The charge then reads the amount from the snapshot it is handed, which is the server's stored copy,
already validated. See [State Machines: Persistence](/docs/statemachine#persistence-and-exactly-once-effects).

### The forward migration

Version 2 added `total`. A draft an older host stored at version 1 has none, so it would fail the v2
invariants; `MigrateFrom(1, ...)` backfills it from the item count whenever the server reads a version 1
snapshot: a stored draft that `get` reads or an event moves:

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
v1 to v2 migration, the refused save, `AddItem` pricing the cart, the guards on an empty cart, the exported IR,
the canonical wire, and the total check.

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
  1 draft through it reproduces a row the old host left behind, which `get` then upgrades. Drafts are
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
