---
title: Author and serve a machine
description: "Author a declarative state machine with caller rules and an exactly-once effect, wire it into a host, and serve it to clients as its own typed GraphQL API."
type: how-to
area: machines
nav_order: 1
source:
  - Trax.Effect/src/Trax.Effect.StateMachine/Fluent.cs
  - Trax.Effect/src/Trax.Effect.StateMachine.Persistence/StateMachinesBuilderExtensions.cs
---

# Author and serve a machine

This builds a checkout machine (`Cart`, `Review`, `Paid`) whose `Pay` event charges a card exactly once, registers
it in a host, and serves it to clients under the `machines` GraphQL root. What a machine is, and why the snapshot is
shaped the way it is, is in [State machines](/docs/statemachine).

## 1. Declare the machine

Subclass `Machine<TState, TTrigger>` over your own state and trigger enums, and declare everything in `Configure`:
each state's context record, each event's guard, reducer and caller rule, the committed state, and the one
irreversible effect. Mark the class `[TraxMachine]` to serve it to clients; an unmarked machine is reachable only from
host code.

A served machine has to be declarative (guards and reducers written as data, so it has an
[IR](/docs/sdk-reference/statemachine-api/ir-format)) and user-owned, and it must declare who may do everything a
client can do: a rule on every event and on its `Start`, and an owner-only or stricter rule on `Get`, `Watch` and
`Discard`, with `Watch` at least as strict as `Get`, since the push carries what `get` answers. The host refuses to start, listing every gap in one exception, until it does.

```csharp compile=checkout
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

public enum CheckoutState
{
    Cart,
    Review,
    Paid,
}

public enum CheckoutTrigger
{
    ChooseItems,
    Next,
    Back,
    Pay,
}

public sealed record EmptyStart;

public sealed record CartContext
{
    public List<string> Items { get; init; } = [];
}

public sealed record PaidContext
{
    public List<string> Items { get; init; } = [];

    public string Receipt { get; init; } = "";
}

public sealed record ItemsInput
{
    public List<string> Items { get; init; } = [];
}

public sealed record PayInput
{
    public string Receipt { get; init; } = "";
}

[TraxMachine]
public sealed class CheckoutMachine : Machine<CheckoutState, CheckoutTrigger>
{
    private static readonly Rule HasItems = Field((CartContext c) => c.Items).CountGreaterThan(0);

    protected override void Configure(IMachineBuilder<CheckoutState, CheckoutTrigger> m)
    {
        m.Id("checkout")
            .Version(1)
            .StartsAt(CheckoutState.Cart, () => new JsonObject { ["items"] = new JsonArray() })
            .Start<EmptyStart>(s => s.Reduce(Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(CheckoutState.Cart)
            .Context<CartContext>()
            .On(CheckoutTrigger.ChooseItems)
            .AnyAuthenticated()
            .WithInput<ItemsInput>()
            .Reduce(Set((CartContext c) => c.Items).FromInput((ItemsInput i) => i.Items))
            .To(CheckoutState.Cart)
            .On(CheckoutTrigger.Next)
            .AnyAuthenticated()
            .When(HasItems)
            .Because("Choose an item before reviewing.")
            .To(CheckoutState.Review);

        m.In(CheckoutState.Review)
            .Context<CartContext>()
            .On(CheckoutTrigger.Back)
            .AnyAuthenticated()
            .To(CheckoutState.Cart)
            .On(CheckoutTrigger.Pay)
            .AnyAuthenticated()
            .WithInput<PayInput>()
            .When(All(HasItems, Input((PayInput i) => i.Receipt).NonEmpty()))
            .Because("Checkout needs items and a receipt to be paid.")
            .RunsOnce<ICharge>("checkout:charge")
            .Reduce(Set((PaidContext c) => c.Receipt).FromInput((PayInput i) => i.Receipt))
            .To(CheckoutState.Paid);

        m.In(CheckoutState.Paid).Context<PaidContext>().Committed();
    }
}
```

`Pay` is bound to the effect, so no client fires it with a receipt: the server runs the charge and fires `Pay` with
the receipt the charge returned, as `input["receipt"]`. `Paid` is committed, so no save can put a draft there or take
one out. Every builder call is listed in [Machine authoring](/docs/sdk-reference/statemachine-api/machine),
the guard and reduction forms in the [Rules vocabulary](/docs/sdk-reference/statemachine-api/rules), and the
records' mapping to JSON in [Context and input schemas](/docs/statemachine/context-schemas).

## 2. Implement the effect

The effect is an interface over `ISnapshotEffect`, implemented in the host. The runner calls the overload that takes
the effect's key, which is the same for every run of one send; pass it to the system you call as its idempotency
key. The effect gets the server's stored snapshot, never the client's, so price from what the server holds.

```csharp compile=checkout
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

public interface ICharge : ISnapshotEffect;

public sealed class GatewayCharge(IPaymentGateway gateway) : ICharge
{
    public Task<string> Run(Snapshot snapshot, string effectKey, CancellationToken cancellationToken)
    {
        var items = snapshot.Context["items"]!.AsArray().Select(i => i!.GetValue<string>()).ToList();
        return gateway.Charge(items, idempotencyKey: effectKey, cancellationToken);
    }

    public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Trax runs a machine's effect with its key.");
}
```

Return a non-empty receipt. Throwing means the charge did not happen and a retry runs it again; an
`OperationCanceledException` means the outcome is unknown, and the effect is taken over under the same key once its
lease passes. The details are in [Effects](/docs/sdk-reference/statemachine-api/i-snapshot-effect).

## 3. Map the caller to a user key

Drafts are scoped by a user key, which the host supplies through `ISnapshotPrincipal`. Over Trax's `TraxCaller`:

```csharp compile=checkout
using Trax.Api.Auth;
using Trax.Effect.StateMachine.Persistence;

public sealed class TraxCallerSnapshotPrincipal(TraxCaller caller) : ISnapshotPrincipal
{
    public string? CurrentUserKey => caller.IsAuthenticated ? caller.Principal!.Id : null;
}
```

A request with no user key is refused as `UNAUTHENTICATED`. Authentication itself is the host's: see
[Secure a GraphQL host](/docs/security/secure-a-graphql-host).

## 4. Register the machine in the host

`AddStateMachines` discovers every machine in the assemblies it is given and wires the snapshot store, the
effect-claim ledger, the exactly-once runner and the registry over the data provider configured in `AddEffects`.
Call it after `AddEffects` and before `AddMediator`. A served machine's operations are persisted documents that
carry the machine's version, so the GraphQL host must call `UsePersistedOperations`; a host that serves a machine
without it refuses to start.

```csharp compile=checkout
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddStateMachines(typeof(CheckoutMachine).Assembly)
        .AddMediator(typeof(CheckoutMachine).Assembly));

builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
builder.Services.AddScoped<ICharge, GatewayCharge>();

builder.Services.AddTraxGraphQL(graphql =>
    graphql.UsePersistedOperations(persisted => persisted.SingleNode()));
```

`SingleNode()` suits one process; more than one node uses `UseRabbitMqInvalidation(connectionString)`, as
[Turn on persisted operations](/docs/api/persisted-operations) describes. Host options (draft expiry, the per-user draft
cap, the change feed) are under [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines#options).
The rules for which registered machines are served, and the refusals for a marked subclass or a duplicate id, are
under [Serving a machine](/docs/sdk-reference/statemachine-api/add-state-machines#serving-a-machine).

## 5. Call it from a client

The machine is now `machines { checkout { ... } }`: `start`, one mutation per client event (`chooseItems`, `next`,
`back`), the effect event `pay`, `discard`, and the `get` query, each answering with the state, its typed context and
the revision, or a `problem`. With persisted operations required (the default), a client sends each operation by
its persisted id, which it reads from the exported [client manifest](/docs/sdk-reference/statemachine-api/client-manifest) at build time; the
[TypeScript client](/docs/statemachine/call-a-machine-from-typescript) does that from the machine's
documents. The `chooseItems` operation, written out as a document (the generated one selects every field of the
answer):

```graphql
mutation ($id: UUID!, $input: ItemsInput!, $requestId: String!) {
  machines { checkout { chooseItems(id: $id, input: $input, requestId: $requestId) {
    id revision state context { items receipt } problem { code message } } } }
}
```

Every refusal is a typed `problem` in the data, never a thrown error: `GUARD_FAILED` carries the `Because` text,
`FORBIDDEN` a caller the event's rule refuses, `REQUEST_ID_REUSED` a retry that changed its request. Fields, answers
and limits are in the [machines root](/docs/sdk-reference/graphql-api/machines), and every code in
[Result codes](/docs/sdk-reference/statemachine-api/result-codes).

[12. Checkout is a state machine](/docs/book/checkout-is-a-state-machine) builds one step by step and serves it.
The [State Machine sample](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/samples/StateMachine) is another runnable version: two machines, a GraphQL host, exactly-once over the
wire, a forward migration and a server-checked total.

## Serve the generic mutations instead

A host can also serve six generic `stateMachine` mutations that take the machine as an argument. They are off by
default, and the opt-in requires an authorization policy:

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddStateMachines(
            o => o.ExposeGenericStateMachineMutations(g => g.Policy = "machine-clients"),
            assembly)
        .AddMediator(assembly));
```

The host registers the policy with ASP.NET Core authorization, as for a train's `[TraxAuthorize(Policy = ...)]`.
While the opt-in is on, every user-owned machine is reachable through the generic mutations, so each must declare the
same rules a marked machine does. The mutations are listed in
[Generic state-machine mutations](/docs/statemachine/generic-mutations).

## Keep the two runtimes in parity

A TypeScript twin and the differential corpus that proves it matches the server are generated from the machine's IR
by `trax machine generate`; [The codegen pipeline](/docs/statemachine/codegen-pipeline) explains both and how to
author the corpus's inputs with `.Differential(...)`.

## SDK Reference

> [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Machine authoring](/docs/sdk-reference/statemachine-api/machine) | [Effects](/docs/sdk-reference/statemachine-api/i-snapshot-effect) | [Persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports) | [Machines root](/docs/sdk-reference/graphql-api/machines) | [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations)
