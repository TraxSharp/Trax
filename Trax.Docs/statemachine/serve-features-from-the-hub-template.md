---
title: Serve features from the hub template
description: "Serve machines written as features from a trax-hub project: packages, ServeFeatures, the machines folder, ISnapshotPrincipal, persisted operations, export."
type: how-to
area: machines
nav_order: 25
---

# Serve features from the hub template

`dotnet new trax-hub` gives a host that runs trains. Serving the machines `trax machine from-gherkin` derives
([Write machines as features](/docs/statemachine/write-machines-as-features)) takes the steps below, in this order.
The [CheckIn sample's host](https://github.com/TraxSharp/Trax/blob/main/Trax.Samples/samples/CheckIn/Trax.Samples.CheckIn.Api/Program.cs)
has every one of them, on Postgres.

## 1. Add the packages

```bash
dotnet add package Trax.Effect.Data.Sqlite --version <the version Trax.Effect has>
dotnet add package Trax.Api.GraphQL.PersistedOperations --version <the version Trax.Effect has>
```

| Package | Why |
|---|---|
| `Trax.Effect.Data.Sqlite`, or `Trax.Effect.Data.Postgres` | Without `ConnectionStrings:Trax` the template runs on `UseInMemory()`, and a host whose features have policies refuses to start on it: InMemory has no transaction to queue a policy's fire in. `UseSqlite` is in `Trax.Effect.Data.Sqlite`, `UsePostgres` in `Trax.Effect.Data.Postgres`. |
| `Trax.Api.GraphQL.PersistedOperations` | A host that serves machines refuses to start without persisted operations, and `RunTraxAsync`, which exports the client manifest, is in it too. |

`AddStateMachines`, `ServeFeatures` and `ISnapshotPrincipal` are in `Trax.StateMachine.Persistence`, which
`Trax.Mediator` already brings. Keep every `Trax.*` package at one version
([Project Templates](/docs/reference/templates#creating-projects)).

## 2. Serve the machines

Replace the template's choice between `UseInMemory()` and `UsePostgres` with one database, and call `AddStateMachines` after `AddEffects` and before `AddMediator`:

```csharp compile
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.StateMachine.Persistence;

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UseSqlite(
                    builder.Configuration.GetConnectionString("Trax")
                        ?? throw new InvalidOperationException("Set ConnectionStrings:Trax.")
                )
                .SaveTrainParameters()
                .AddJunctionProgress()
        )
        .AddStateMachines(machines => machines.ServeFeatures("machines", typeof(Program).Assembly))
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler()
);
```

Add `"ConnectionStrings": { "Trax": "Data Source=trax.db" }` to `appsettings.json`. `ServeFeatures` takes the
directory and the assemblies the invoked trains are in, found by their interface's full name. Called after
`AddMediator`, `AddStateMachines` does not compile; called without a data provider, it throws
([AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines#behavior)). The template's
`AddScheduler()` is what runs the trains the machines invoke, and `AddJunctionProgress()` is what lets an instance
leaving an invoking state stop its run.

## 3. Copy the machines into the build output

`ServeFeatures("machines", ...)` reads a relative path from the application's base directory, so the build has to
copy the folder there. With `machines/` beside the project folder, as `from-gherkin --out machines` writes it from
the repository root:

```xml from=Trax.Samples/samples/CheckIn/Trax.Samples.CheckIn.Api/Trax.Samples.CheckIn.Api.csproj
  <ItemGroup>
    <None Include="../machines/*" LinkBase="machines" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

Without it the host refuses to start, since the directory it serves is not there.

## 4. Say whose instances a request reaches

Every machine operation and view reads the caller's user key from `ISnapshotPrincipal`, and each user's instances are
stored under that key. Trax registers no implementation. Without one, every `get` answers null, every view is refused
with `UNAUTHENTICATED`, and no start or move reaches an instance. The CheckIn sample maps the signed-in caller
to their principal's id:

```csharp from=Trax.Samples/samples/CheckIn/Trax.Samples.CheckIn.Api/TraxCallerSnapshotPrincipal.cs
public sealed class TraxCallerSnapshotPrincipal(TraxCaller caller) : ISnapshotPrincipal
{
    public string? CurrentUserKey => caller.IsAuthenticated ? caller.Principal!.Id : null;
}
```

Copy it into the project and register it beside the template's `AddAuthorization()`:

```csharp fragment
builder.Services.AddTraxPrincipalAccessor();
builder.Services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
```

With the template's API keys, the user key is the key's `id` qualified by its scheme: `keys.Add(key, id: "ada",
"passenger")` stores Ada's instances under `TraxApiKey:ada`.
The template registers its keys in Development only, and `AddTraxApiKeyAuth` is what registers `TraxCaller`, so
[`AddTraxPrincipalAccessor()`](/docs/sdk-reference/api-auth/add-trax-principal-accessor) keeps it resolvable in every
environment. See [ISnapshotPrincipal](/docs/sdk-reference/statemachine-api/persistence-ports#isnapshotprincipal).

## 5. Turn on persisted operations

A host that serves machines refuses to start without persisted operations: each machine operation and view is a
document the host registers under an id. Add it to the template's `AddTraxGraphQL`, after the calls already there:

```csharp compile
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;

builder.Services.AddTraxGraphQL(graphql =>
    graphql.UsePersistedOperations(persisted =>
        persisted.SingleNode().RequirePersisted(false).LogNonPersistedRequests(true)
    )
);
```

| Mode | Inline queries | The template |
|---|---|---|
| Strict, `RequirePersisted(true)` or nothing (the default) | Refused with `PERSISTED_OPERATION_REQUIRED`. Introspection still passes. | The GraphQL IDE loads the schema, but every operation typed into it is refused. The README's `helloWorld` and `lookup` calls are refused, and so is the test in `HostTests` that runs `helloWorld` with the demo key. |
| Shadow, `RequirePersisted(false)` with `LogNonPersistedRequests(true)` | Run, and each is logged at Information as `Trax persisted-operations observed inline query`. | Everything works as before. |

`RequirePersisted(false)` alone refuses to start, since it would do nothing. `SingleNode()` says one process serves
the endpoint; with more, see [Turn on persisted operations](/docs/api/turn-on-persisted-operations#6-run-more-than-one-node).
The operations console keeps working in both modes: `UseTraxApiDashboard` registers its own documents. Move to strict
once the clients send only persisted ids.

## 6. Export the client manifest

Replace the template's last line, `app.Run();`, with:

```csharp fragment
await app.RunTraxAsync(args);
```

`RunTraxAsync` (`using Trax.Api.GraphQL.PersistedOperations.Extensions;`) serves the host as `app.Run()` does, or,
started with `--trax-export-client <path>`, writes the client manifest and returns without serving. Then generate the
frontend's client from it:

```bash
dotnet run -- --trax-export-client ../web/src/trax/manifest.json
trax client --target react --manifest ../web/src/trax/manifest.json --out ../web/src/trax
```

An export opens no database. A host that seeds data at startup checks `TraxClientExport.Requested` first
(`using Trax.Effect.Configuration.ClientExport;`); the template's `EnsureCreated()` on its in-memory context does no
harm. See [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) and
[`trax client`](/docs/reference/cli#frontend-clients-trax-client).

## 7. Shorten the sweeps for timers in seconds

A feature that says `When 2 seconds pass` is served on the host's own pace, not to the second. Two background loops
set it:

| Option | Default | What it bounds |
|---|---|---|
| `PolicyRunnerOptions.Interval`, set with `PolicyRunner(...)` | 1 second | How long the policy runner waits between passes when nothing is owed, so how late a policy fires after the move that owes it. |
| `InvokeOutcomeSweepInterval` | 5 seconds | How often the outcome reconciler sweeps. The sweep fires due timers and wakes for the next one it saw, but a timer armed since the last sweep is found at the next. |
| `InvokeOutcomeSweepIntervalWhileListening` | 1 minute | The sweep's pace on Postgres while it hears the notices of invoked runs ending. Never shorter than `InvokeOutcomeSweepInterval`. |

For timers a few seconds long, shorten all three:

```csharp compile
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.StateMachine.Persistence;

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UseSqlite("Data Source=trax.db"))
        .AddStateMachines(machines =>
        {
            machines.ServeFeatures("machines", typeof(Program).Assembly);
            machines.InvokeOutcomeSweepInterval = TimeSpan.FromSeconds(1);
            machines.InvokeOutcomeSweepIntervalWhileListening = TimeSpan.FromSeconds(1);
            machines.PolicyRunner(policies => policies.Interval = TimeSpan.FromMilliseconds(250));
        })
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler()
);
```

A policy fires in a later pass of the runner, not in the transaction of the move that owes it, so on the host the
policies a move sets off and the outcomes of the trains it invokes land in no fixed order. `trax machine run` plays
them in the order the scenario reads, so a design that depends on that order passes in memory and not on the host.
Every option is on [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines#options).

## SDK Reference

> [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Persistence ports](/docs/sdk-reference/statemachine-api/persistence-ports) | [UseSqlite](/docs/sdk-reference/configuration/use-sqlite) | [UsePersistedOperations](/docs/sdk-reference/persisted-operations/use-persisted-operations) | [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) | [AddTraxPrincipalAccessor](/docs/sdk-reference/api-auth/add-trax-principal-accessor)
