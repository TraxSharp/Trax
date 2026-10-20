---
name: trax-start-a-project
description: Use when creating a new app on Trax .NET or adding Trax to an existing ASP.NET Core app. Covers choosing between the trax-api, trax-hub and trax-scheduler dotnet new templates, installing Trax.Samples.Templates, scaffolding from a GraphQL or OpenAPI schema with trax generate, the project layout, registration order in Program.cs, moving off UseInMemory to SQLite or Postgres, and pinning every Trax package at one version in Directory.Packages.props.
metadata:
  internal: true
---

# Start a Trax project

Use this when the task is a new app on Trax, or the first Trax packages in an existing one. Load `trax` first if you
have not; it holds the rules that hold everywhere.

## Decide first

**From a template, from a schema, or into an existing app.**

| Situation | Start with |
|---|---|
| A new app, no existing API to match | A template: `dotnet new trax-hub` (or `trax-api`, `trax-scheduler`) |
| A new app that implements an existing GraphQL SDL or OpenAPI 2.0/3.0 document | `trax generate` (below), which scaffolds `trax-hub` plus a trains library |
| An existing ASP.NET Core app | No template. Add the packages and follow [Getting started](https://traxsharp.net/docs/getting-started) from step 2 |

**Which template.** Pick by what the app has to do now, not by what the brief calls it.

| The app | Template |
|---|---|
| Serves trains over GraphQL, each run when it is called, nothing on a timer | `trax-api` |
| Runs trains on a schedule, with the operations console, and serves none of its own | `trax-scheduler` |
| Both, or a state machine that invokes trains, or `mode: QUEUE` mutations | `trax-hub` |

A [state machine that invokes trains](https://traxsharp.net/docs/statemachine/invoking-trains) needs a scheduler,
`AddJunctionProgress()` and a database. `trax-hub` has the first two; `trax-api` has neither. Starting from
`trax-api` and adding them later works, but it is the wrong default. Details per template are on
[Project templates](https://traxsharp.net/docs/reference/templates#which-one-to-start-from).

## Steps

1. **Install the templates at the release you will use.** `dotnet new install Trax.Samples.Templates` installs the
   latest, for your user, not for the project. To match a version the app already uses:
   `dotnet new install Trax.Samples.Templates::<version>`.
2. **Create the project.** `dotnet new trax-hub --name MyCompany.Hub`. The template takes no other parameters.
   Run it once (`dotnet run`, then open `http://localhost:5400/trax/graphql`) and run its tests
   (`dotnet test tests/MyCompany.Hub.Tests`; `dotnet test` at the root finds only the app) before changing anything.
3. **Read [Organize trains in a project](https://traxsharp.net/docs/core/organize-trains)** before writing a train.
   One folder per train under `Trains/`, its own input type, an `I<Name>Train` interface, junctions in `Junctions/`,
   other types in `Models/`, shared code only once two trains need it.
4. **Keep the sample trains** (`HelloWorld`, `Lookup`) until your first train is in and matches their layout file
   for file. They are the style reference; the host tests exercise them. Delete them, and the `hello-world` schedule
   in `Program.cs`, after that. Adding a train is adding its folder:
   [Add your own trains to a template](https://traxsharp.net/docs/reference/add-trains-to-a-template).
5. **Choose a database before the first feature that needs one.** The templates use `UseInMemory()`: runs vanish on
   restart, two processes never share them, `mode: QUEUE` is refused with `TRAX_QUEUE_UNAVAILABLE`, and invoked runs
   never run. See [Move off the in-memory provider](#move-off-the-in-memory-provider).
6. **Before deploying**, read
   [Prepare a template for deployment](https://traxsharp.net/docs/reference/prepare-a-template-for-deployment). The
   demo keys and the console exist only in Development, and the host refuses to start elsewhere with a demo key
   registered. Register real credentials; do not remove the `IsDevelopment()` checks to get past it.

## trax generate

`trax generate` is for an app that implements an existing schema. It always needs one:

```bash
dotnet tool install Trax.Cli --version <the Trax version>
trax generate --schema ./schema.graphql --output ./MyProject --name MyProject
```

Options are `--schema`, `--output`, `--name` (all required), `--type graphql|openapi` and `--force`. It runs
`dotnet new trax-hub`, so the template must be installed, and writes `MyProject/MyProject.Hub/` and a class library
`MyProject/MyProject.Trains/` with one train per operation and junctions that throw `NotImplementedException`.
OpenAPI 3.1 is refused; export 3.0. `--force` is refused for the current directory, its parents and any folder with
a `.git` in it. The library's csproj carries the Trax versions itself, copied from the hub's
`Directory.Packages.props`, so a later bump changes both. See [CLI](https://traxsharp.net/docs/reference/cli#usage).

## Registration order

`Program.cs` in `trax-hub` is the working order, each call commented with the error its removal raises. What must
come first: `AddTrax()` before `AddTraxGraphQL()`, and every train registered (by `AddMediator`) before
`AddTraxGraphQL()`. Every exposed train carries `[TraxAuthorize]` or `[TraxAllowAnonymous]`. Everything else is
order-free. When the host refuses to start, the message names the call; the full table is
[Registration order](https://traxsharp.net/docs/reference/registration-order).

`AddMediator(typeof(Program).Assembly)` registers every train in that assembly. Trains moved into a class library
need `AddMediator(typeof(SomeTypeInTheLibrary).Assembly)` instead, or they do not exist
([Structuring your application](https://traxsharp.net/docs/reference/project-layout)).

## Move off the in-memory provider

Replace `UseInMemory()` in `AddEffects` and add the provider package at the version every other `Trax.*` package has.
Trax creates and migrates its own `trax` tables at startup. SQLite, for one host:

```csharp compile
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UseSqlite(
                    builder.Configuration.GetConnectionString("TraxDatabase")
                        ?? throw new InvalidOperationException("Set ConnectionStrings:TraxDatabase.")
                )
                .SaveTrainParameters()
                .AddJunctionProgress()
        )
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler()
);
```

Postgres, for more than one process on one database: `UsePostgres(...)` from `Trax.Effect.Data.Postgres`
(namespace `Trax.Effect.Data.Postgres.Extensions`), steps on
[Switch a template to Postgres](https://traxsharp.net/docs/reference/switch-a-template-to-postgres). SQLite does not
coordinate dispatch across processes ([UseSqlite](https://traxsharp.net/docs/sdk-reference/configuration/use-sqlite)).

The app's own `AppDbContext` is separate and stays on `UseInMemoryDatabase("app")` until you move it too.

## Packages

Every `Trax.*` package is at one version, pinned once in `Directory.Packages.props`, which the templates already
use. `dotnet add package <Name> --version <version>` writes the pin there. Never float (`Version="1.*"`). If the
app pins EF Core, Npgsql, HotChocolate or `Microsoft.Extensions.*` itself and restore fails with `NU1605` or
`NU1109`, raise only the packages the error names to the floors on
[Packages](https://traxsharp.net/docs/reference/packages#requirements).

## Known mistakes

- **Deleting `HelloWorld` and `Lookup` first**, then writing trains from memory: five types in one file, no train
  folders, junctions shared before a second train needed them. Keep the samples until your train matches them.
- **Choosing `trax-api` for a machine that invokes trains.** It has no scheduler and no `AddJunctionProgress()`;
  the run is queued and nothing ever picks it up. Use `trax-hub` on SQLite or Postgres.
- **Copying the samples' `public` junctions.** The template's junctions are `public`; the organize page makes them
  `internal`, which is what to write in your own trains.
- **Pointing `AppDbContext` at Trax's SQLite file.** `EnsureCreated()` sees Trax's tables, decides the database
  exists and creates none of yours, with no error. Give the app its own file.
- **SQLite files in `data/`** beside the `Data/` source folder: on macOS and Windows those are one folder. Use a
  folder such as `App_Data/`, and create it before the host opens the file.
- **A new package at a different version**, or the latest one from `dotnet add package` without `--version`. Match
  the version the other `Trax.*` packages have.
- **Running `dotnet test` at the project root** and reading "no tests" as passing. Name the test project.

## Pages

> [AddTrax](https://traxsharp.net/docs/sdk-reference/configuration/add-trax) | [UseInMemory](https://traxsharp.net/docs/sdk-reference/configuration/use-in-memory) | [UseSqlite](https://traxsharp.net/docs/sdk-reference/configuration/use-sqlite) | [UsePostgres](https://traxsharp.net/docs/sdk-reference/configuration/use-postgres) | [SaveTrainParameters](https://traxsharp.net/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionProgress](https://traxsharp.net/docs/sdk-reference/configuration/add-junction-progress) | [AddMediator](https://traxsharp.net/docs/sdk-reference/configuration/add-mediator) | [AddScheduler](https://traxsharp.net/docs/sdk-reference/scheduler-api/add-scheduler) | [AddTraxGraphQL](https://traxsharp.net/docs/sdk-reference/graphql-api/add-trax-graphql)
