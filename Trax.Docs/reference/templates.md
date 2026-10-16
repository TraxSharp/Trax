---
title: Project Templates
description: "The trax-api, trax-scheduler and trax-hub dotnet new templates: the files each generates, what its Program.cs registers, its URLs and its tests."
type: reference
area: tooling
nav_order: 2
---

# Project Templates

Trax ships three `dotnet new` templates in the `Trax.Samples.Templates` package. Each one runs
with `dotnet run` and nothing else: they use the in-memory data provider, so no database is needed
until you choose one. Each also ships a README, a `.gitignore` and an NUnit test project.

| Template | Short name | What it is | URL in Development |
|----------|------------|------------|------|
| Trax GraphQL API | `trax-api` | A GraphQL API with a typed query train and a mutation train | `http://localhost:5402` |
| Trax Scheduler with Dashboard | `trax-scheduler` | A scheduler running one train every 20 seconds, with the Trax Dashboard | `http://localhost:5401` |
| Trax Hub | `trax-hub` | The API, the scheduler and the dashboard in one process | `http://localhost:5400` |

`trax generate` scaffolds `trax-hub` (see [CLI](/docs/reference/cli)).

### Which one to start from

A [state machine that invokes trains](/docs/statemachine/invoking-trains) needs a scheduler, `AddJunctionProgress()`
and a database. Start it from `trax-hub`, which has the first two, and switch it from `UseInMemory()` to `UseSqlite()`
or `UsePostgres()` ([Switch a template to Postgres](/docs/reference/switch-a-template-to-postgres)). `trax-api` has
no scheduler.

Before writing your own trains, read [Organize trains in a project](/docs/core/organize-trains): one folder per train,
its own input type, its junctions beside it. The templates' `HelloWorld` and `Lookup` trains follow it, so keep them as
a reference until your first train is in.

## Installation

```bash
dotnet new install Trax.Samples.Templates
```

`dotnet new install` installs for your user, not for the project: the templates are available in every folder until
you [uninstall](#uninstalling) them.

## Creating Projects

```bash
dotnet new trax-api --name MyCompany.Api
dotnet new trax-scheduler --name MyCompany.Scheduler
dotnet new trax-hub --name MyCompany.Hub
```

Each command creates a directory with the namespaces, file names and `.csproj` set to your
project name. The templates take no other parameters.

Package versions live in the generated `Directory.Packages.props` (Central Package Management),
one line per package, so the `.csproj` files carry no versions. They are the versions the template
was built and tested against when the package was released. To add a package, run
`dotnet add package <Name> --version <version>`, which writes the version into
`Directory.Packages.props` for you. Every Trax package releases at one version, and the template pins every `Trax.*` package at the
version it was released with. Keep them all at one version: a new `Trax.Effect.Data.Postgres` goes
in at the version `Trax.Effect` already has.

## What You Get

### trax-hub

```
MyCompany.Hub/
├── MyCompany.Hub.csproj
├── Directory.Packages.props
├── Program.cs
├── README.md
├── .gitignore
├── appsettings.json
├── Properties/
│   └── launchSettings.json
├── Auth/
│   └── DemoKeys.cs
├── Data/
│   ├── AppDbContext.cs
│   ├── AppSchema.cs
│   ├── IAppDbContext.cs
│   └── Models/
│       └── Note.cs
├── Trains/
│   ├── HelloWorld/
│   │   ├── IHelloWorldTrain.cs
│   │   ├── HelloWorldTrain.cs
│   │   ├── HelloWorldInput.cs
│   │   └── Junctions/
│   │       └── LogGreetingJunction.cs
│   └── Lookup/
│       ├── ILookupTrain.cs
│       ├── LookupTrain.cs
│       ├── LookupInput.cs
│       ├── LookupOutput.cs
│       └── Junctions/
│           └── FetchDataJunction.cs
└── tests/
    └── MyCompany.Hub.Tests/
        ├── MyCompany.Hub.Tests.csproj
        ├── UnitTests/
        │   └── FetchDataJunctionTests.cs
        └── IntegrationTests/
            ├── HelloWorldTrainTests.cs
            └── HostTests.cs
```

`Program.cs` registers, in this order, each call commented with what it does and the startup
error its removal causes:

| Registration | What it does | Without it |
|---|---|---|
| `AddTraxApiKeyAuth(...)`, Development only | The demo key `demo-key-do-not-use-in-production`, holding the `User` role | Every operation is refused, as it is in Production |
| `AddAuthentication()`, `AddAuthorization()` | ASP.NET Core authentication and authorization | Outside Development, `UseAuthentication()` throws at startup: `Unable to resolve service for type 'IAuthenticationSchemeProvider'` (`AddTraxApiKeyAuth` registers it in Development) |
| `AddTrax(...)` with `UseInMemory().SaveTrainParameters().AddJunctionProgress()` | Records every run in memory, with its input and output, and the junction it is on | `AddTraxGraphQL()` throws: `AddTraxGraphQL() requires AddTrax() to be called first` |
| `.AddMediator(typeof(Program).Assembly)` | Registers every train in the project under its interface | The trains do not exist; `Schedule<IHelloWorldTrain>` refuses to start: `No train implements IServiceTrain<HelloWorldInput, TOut>` |
| `.AddScheduler(...)` with `Schedule<IHelloWorldTrain>("hello-world", ..., Every.Seconds(20))` | Stores the manifest at startup and runs it every 20 seconds | `UseTraxDashboard()` refuses to start: `UseTraxDashboard() requires the Trax Scheduler` |
| `AddDbContextFactory<AppDbContext>(...)` | Your own EF Core context, on an in-memory database | The `EnsureCreated()` bootstrap throws: `No service for type 'IDbContextFactory<AppDbContext>' has been registered` |
| `AddTraxGraphQL(graphql => graphql.AddDbContext<AppDbContext>())` | The schema at `/trax/graphql`: trains under `discover` and `dispatch`, `Note` under `discover { app }` | `UseTraxGraphQL()` throws: `UseTraxGraphQL() requires AddTraxGraphQL() to be called first.` |
| `AddHealthChecks().AddTraxHealthCheck()` | `/trax/health` | `MapHealthChecks` throws, naming `AddHealthChecks` |
| `AddTraxDashboard(d => d.AllowAnonymousDashboard())`, Development only | The dashboard at `/trax`, open to anyone who can reach the port | Without the posture, `UseTraxDashboard()` throws naming `RequirePolicy`, `RequireRoles` and `AllowAnonymousDashboard`; without `AddTraxDashboard` at all, it throws `UseTraxDashboard() requires AddTraxDashboard() to be called first.` |

`AllowAnonymousDashboard()` sits inside the `IsDevelopment()` check, and so do `AddTraxDashboard`
and `UseTraxDashboard`: the dashboard is not served anywhere else until you choose who may use it
(see [Prepare a template for deployment](/docs/reference/prepare-a-template-for-deployment)).

The two trains both carry `[TraxAuthorize(Roles = "User")]`:

- **LookupTrain**: `[TraxQuery]`, a query that runs the train and returns its output: `query { discover { lookup(input: { id: "42" }) { id name createdAt } } }`
- **HelloWorldTrain**: `[TraxMutation(GraphQLOperation.Run)]`, a mutation that runs the train and answers when it finishes: `mutation { dispatch { helloWorld(input: { name: "Trax" }) { externalId metadataId } } }`

`HelloWorldTrain` exposes `Run` only on purpose. `[TraxMutation]` with no operation also adds a
`mode: QUEUE` argument, which writes the run to the scheduler's work queue. Only a database-backed
scheduler reads that queue: on the in-memory provider nothing would ever run the entry, so a
`mode: QUEUE` request is refused with `TRAX_QUEUE_UNAVAILABLE` and the host logs a warning at
startup. [Switch to Postgres](/docs/reference/switch-a-template-to-postgres) before enabling `Queue`.

The junctions derive `EffectJunction<TIn, TOut>`, because `AddJunctionProgress()` only sees
junctions of that type. See [Junction Progress](/docs/effect/effect-providers/junction-progress).

**Packages:** `Trax.Core`, `Trax.Effect`, `Trax.Effect.Data.InMemory`, `Trax.Effect.Provider.Parameter`,
`Trax.Effect.JunctionProvider.Progress`, `Trax.Mediator`, `Trax.Scheduler`, `Trax.Dashboard`,
`Trax.Api`, `Trax.Api.Auth.ApiKey`, `Trax.Api.GraphQL`.

### trax-api

The `Auth/`, `Data/`, `Trains/` and `tests/` of `trax-hub`, in a host with no scheduler and no
dashboard. `Program.cs` registers the demo key, `AddTrax` with
`UseInMemory().SaveTrainParameters()` and `AddMediator`, the application context, GraphQL and the
health check. The tests have no dashboard checks.

The API runs every train itself, when it is called. Running trains on a timer, or queueing them
for another process, needs a scheduler with the same trains registered and a database both
processes share; `trax-hub` is the one-process version.

**Packages:** `Trax.Core`, `Trax.Effect`, `Trax.Effect.Data.InMemory`, `Trax.Effect.Provider.Parameter`,
`Trax.Mediator`, `Trax.Api`, `Trax.Api.Auth.ApiKey`, `Trax.Api.GraphQL`.

### trax-scheduler

```
MyCompany.Scheduler/
├── MyCompany.Scheduler.csproj
├── Directory.Packages.props
├── Program.cs
├── README.md
├── .gitignore
├── appsettings.json
├── Properties/
│   └── launchSettings.json
├── Trains/
│   └── HelloWorld/
│       ├── IHelloWorldTrain.cs
│       ├── HelloWorldTrain.cs
│       ├── HelloWorldInput.cs
│       └── Junctions/
│           └── LogGreetingJunction.cs
└── tests/
    └── MyCompany.Scheduler.Tests/
        ├── MyCompany.Scheduler.Tests.csproj
        └── IntegrationTests/
            ├── HelloWorldTrainTests.cs
            └── HostTests.cs
```

`Program.cs` registers `AddTrax` with `UseInMemory().SaveTrainParameters().AddJunctionProgress()`,
`AddMediator`, `AddScheduler` with the `hello-world` manifest every 20 seconds, and the dashboard
with `AllowAnonymousDashboard()` in Development only. It has no GraphQL and no authentication.

**Packages:** `Trax.Core`, `Trax.Effect`, `Trax.Effect.Data.InMemory`, `Trax.Effect.Provider.Parameter`,
`Trax.Effect.JunctionProvider.Progress`, `Trax.Mediator`, `Trax.Scheduler`, `Trax.Dashboard`.

## Running

```bash
cd MyCompany.Hub && dotnet run
```

`dotnet run` starts in Development, from the `http` profile in `Properties/launchSettings.json`,
which is also the only place the URL is set:

| Template | Open |
|----------|------|
| `trax-hub` | `http://localhost:5400/trax/graphql` for the GraphQL IDE, `http://localhost:5400/trax` for the dashboard, `/trax/health` |
| `trax-api` | `http://localhost:5402/trax/graphql` for the GraphQL IDE, `/trax/health` |
| `trax-scheduler` | `http://localhost:5401/trax` for the dashboard |

Send `X-Api-Key: demo-key-do-not-use-in-production` with every GraphQL operation:

```bash
curl -X POST http://localhost:5400/trax/graphql \
  -H 'Content-Type: application/json' \
  -H 'X-Api-Key: demo-key-do-not-use-in-production' \
  -d '{"query":"mutation { dispatch { helloWorld(input: { name: \"Trax\" }) { externalId metadataId } } }"}'
```

Without the key, or with a wrong one, the response is HTTP 200 with an error whose
`extensions.code` is `TRAX_AUTHORIZATION`.

The HelloWorld train runs every 20 seconds in `trax-scheduler` and `trax-hub`; the dashboard shows
each run. The in-memory provider keeps its data inside the process, so it is lost on restart and
two processes never see each other's.

Outside `dotnet run` (a published build, a container) the URL comes from `ASPNETCORE_URLS` or
`ASPNETCORE_HTTP_PORTS`, and with neither set Kestrel listens on `http://localhost:5000`.

## Testing

```bash
dotnet test tests/MyCompany.Hub.Tests
```

`dotnet test` at the project root finds only the application project, so name the test project.
Each test project references the application and uses NUnit with
`Microsoft.AspNetCore.Mvc.Testing`:

| Test | What it shows |
|---|---|
| `UnitTests/FetchDataJunctionTests` | A junction constructed with its dependencies and called directly: no container, no database |
| `IntegrationTests/HelloWorldTrainTests` | A train run through `ITrainBus` on a container with `UseInMemory()` and `AddMediator`, then its `Metadata` read back from `IDataContext` |
| `IntegrationTests/HostTests` | `WebApplicationFactory<Program>` starting the whole host in Development and in Production, calling GraphQL with and without the demo key, and checking the dashboard is served only in Development |

`HostTests` is the test that fails when `Program.cs` no longer starts. The application's top-level
`Program` class is public in .NET 10, so `WebApplicationFactory<Program>` needs nothing added. See
[Testing](/docs/cross-cutting/testing) for more patterns.

## Next steps

- [Organize trains in a project](/docs/core/organize-trains): where each train, input and junction goes.
- [Add your own trains to a template](/docs/reference/add-trains-to-a-template): a query train and a scheduled train.
- [Switch a template to Postgres](/docs/reference/switch-a-template-to-postgres): a database that survives a restart and lets `mode: QUEUE` run.
- [Prepare a template for deployment](/docs/reference/prepare-a-template-for-deployment): the dashboard and the demo key exist only in Development.

## Uninstalling

```bash
dotnet new uninstall Trax.Samples.Templates
```

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard) | [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddTraxApiKeyAuth](/docs/sdk-reference/api-auth/add-trax-api-key-auth)
