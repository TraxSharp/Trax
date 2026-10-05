# Trax.Samples.Scheduler

A [Trax](https://traxsharp.net/docs) scheduler: it runs your trains on a timer and shows every run
in the Trax dashboard. It runs with no database: the in-memory provider keeps every run in this
process and loses it on restart.

| Path | What it is |
|---|---|
| `Program.cs` | The whole host, every registration commented with what it does and what fails without it |
| `Trains/HelloWorld/` | A train that logs a greeting, scheduled every 20 seconds |
| `tests/Trax.Samples.Scheduler.Tests/` | NUnit tests: a train run through the bus, and the host started in Development and Production |
| `Directory.Packages.props` | Every package version, in one place |

## Run it

```bash
dotnet run
```

`dotnet run` starts in Development (from `Properties/launchSettings.json`). Open
`http://localhost:5401/trax` for the dashboard: the `hello-world` manifest, its next run, every run
so far, and buttons to run it now, disable it or cancel a run. The console logs
`Hello, Trax!` every 20 seconds.

## Run the tests

```bash
dotnet test tests/Trax.Samples.Scheduler.Tests
```

`IntegrationTests/HelloWorldTrainTests` runs the train through `ITrainBus` and reads the record
Trax kept of it. `IntegrationTests/HostTests` starts the application in Development and in
Production and checks that the dashboard is served only in Development; it is the test that fails
when `Program.cs` stops starting. See [Testing](https://traxsharp.net/docs/cross-cutting/testing).

## Add a scheduled train

A train is a chain of junctions; each junction takes one input and produces one output. Put each
train in its own folder under `Trains/`. `AddMediator(typeof(Program).Assembly)` registers every
train in this project. A scheduled train's input implements `IManifestProperties`, so the
scheduler can store it.

```csharp
// Trains/Cleanup/CleanupInput.cs
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduler.Trains.Cleanup;

public record CleanupInput : IManifestProperties
{
    public int OlderThanDays { get; init; } = 30;
}
```

```csharp
// Trains/Cleanup/Junctions/DeleteOldRowsJunction.cs
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Scheduler.Trains.Cleanup.Junctions;

public class DeleteOldRowsJunction(ILogger<DeleteOldRowsJunction> logger)
    : EffectJunction<CleanupInput, Unit>
{
    public override Task<Unit> Run(CleanupInput input)
    {
        logger.LogInformation("Deleting rows older than {Days} days", input.OlderThanDays);
        return Task.FromResult(Unit.Default);
    }
}
```

```csharp
// Trains/Cleanup/ICleanupTrain.cs and CleanupTrain.cs
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Scheduler.Trains.Cleanup.Junctions;

namespace Trax.Samples.Scheduler.Trains.Cleanup;

public interface ICleanupTrain : IServiceTrain<CleanupInput, Unit>;

public class CleanupTrain : ServiceTrain<CleanupInput, Unit>, ICleanupTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<DeleteOldRowsJunction>().Resolve();
}
```

Then schedule it in `Program.cs`, after the `hello-world` one (add
`using Trax.Samples.Scheduler.Trains.Cleanup;`):

```csharp
.Schedule<ICleanupTrain>("cleanup", new CleanupInput(), Cron.Daily(hour: 3))
```

Cron times are UTC. The host refuses to start when `Schedule<T>` names a train `AddMediator` did not
register, or when a junction needs an input nothing before it produces. See
[Scheduling](https://traxsharp.net/docs/scheduler) for dependent trains, retries and dead letters.

## Keep runs in Postgres

Start a database (`127.0.0.1` keeps it off your network):

```bash
docker run -d --name trax-db -p 127.0.0.1:5432:5432 \
  -e POSTGRES_USER=trax -e POSTGRES_PASSWORD=trax123 -e POSTGRES_DB=trax postgres:17
```

Add the provider at the same version as `Trax.Effect` in `Directory.Packages.props`; `dotnet add
package` writes the version there for you:

```bash
dotnet add package Trax.Effect.Data.Postgres --version <the Trax.Effect version>
```

In `appsettings.Development.json` (create it), add
`"ConnectionStrings": { "TraxDatabase": "Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123" }`,
then in `Program.cs` add `using Trax.Effect.Data.Postgres.Extensions;` and replace `UseInMemory()`:

```csharp
effects.UsePostgres(
    builder.Configuration.GetConnectionString("TraxDatabase")
        ?? throw new InvalidOperationException("Set ConnectionStrings:TraxDatabase.")
)
```

Trax creates and migrates its `trax` schema at startup. With Postgres the manifests and runs
survive a restart, changes made from the dashboard are kept, and several scheduler processes can
share the work. See
[Data Persistence](https://traxsharp.net/docs/effect/effect-providers/data-persistence).

## Before deploying

The dashboard exists only in Development. Started any other way
(`dotnet bin/.../Trax.Samples.Scheduler.dll`, a container), the application runs in Production:
the trains still run on schedule, and `/trax` is a 404.

- Add authentication, choose who may use the dashboard, `RequirePolicy("<policy>")` or
  `RequireRoles("<role>")`, in place of `AllowAnonymousDashboard()`, then remove the two
  `IsDevelopment()` checks around it. See [Dashboard](https://traxsharp.net/docs/dashboard).
- Outside `dotnet run` the URL comes from `ASPNETCORE_URLS` (or `ASPNETCORE_HTTP_PORTS`), not
  `launchSettings.json`.

## Where to go next

- [Project Templates](https://traxsharp.net/docs/reference/templates): every file this template generated
- [Getting Started](https://traxsharp.net/docs/getting-started): a Trax server built step by step from an empty folder
- [Scheduling](https://traxsharp.net/docs/scheduler): cron and interval schedules, dependent trains, retries, dead letters
- [Registration Order](https://traxsharp.net/docs/reference/registration-order): what must come before what, and the startup errors
- [Samples](https://traxsharp.net/docs/samples): complete applications, one per feature
