# Trax.Samples.Hub

A [Trax](https://traxsharp.net/docs) server in one process: a GraphQL API over your trains, a
scheduler that runs them on a timer, and the Trax dashboard. It runs with no database: the
in-memory provider keeps every run in this process and loses it on restart.

| Path | What it is |
|---|---|
| `Program.cs` | The whole host, every registration commented with what it does and what fails without it |
| `Trains/HelloWorld/` | A `[TraxMutation]` train, also scheduled every 20 seconds |
| `Trains/Lookup/` | A `[TraxQuery]` train that returns typed output |
| `Data/` | Your application's own EF Core context, with one `[TraxQueryModel]` entity (`Note`) |
| `Auth/DemoKeys.cs` | The Development-only demo API key |
| `tests/Trax.Samples.Hub.Tests/` | NUnit tests: a junction, a train run through the bus, and the host started in Development and Production |
| `Directory.Packages.props` | Every package version, in one place |

## Run it

```bash
dotnet run
```

`dotnet run` starts in Development (from `Properties/launchSettings.json`) on
`http://localhost:5400`:

| URL | What |
|---|---|
| `http://localhost:5400/trax/graphql` | The GraphQL IDE |
| `http://localhost:5400/trax` | The dashboard: every run, the `hello-world` schedule, run or cancel |
| `http://localhost:5400/trax/health` | The health check |

Every operation needs the demo key:

```bash
curl -X POST http://localhost:5400/trax/graphql \
  -H 'Content-Type: application/json' \
  -H 'X-Api-Key: demo-key-do-not-use-in-production' \
  -d '{"query":"mutation { dispatch { helloWorld(input: { name: \"Trax\" }) { externalId metadataId } } }"}'
```

```graphql
query { discover { lookup(input: { id: "42" }) { id name createdAt } } }
query { discover { app { notes { nodes { id text } } } } }
```

Without the key, or with a wrong one, an operation answers with a `TRAX_AUTHORIZATION` error.

## Run the tests

```bash
dotnet test tests/Trax.Samples.Hub.Tests
```

`UnitTests/` constructs a junction and calls `Run`. `IntegrationTests/HelloWorldTrainTests` runs
a train through `ITrainBus` and reads the record Trax kept of it. `IntegrationTests/HostTests`
starts the application in Development and in Production and checks what each one serves; it is
the test that fails when `Program.cs` stops starting. See
[Testing](https://traxsharp.net/docs/cross-cutting/testing).

## Add a train

A train is a chain of junctions; each junction takes one input and produces one output. Put each
train in its own folder under `Trains/`. `AddMediator(typeof(Program).Assembly)` registers every
train in this project, so there is nothing to add to `Program.cs` for it to exist.

```csharp
// Trains/Shout/ShoutInput.cs and ShoutOutput.cs
namespace Trax.Samples.Hub.Trains.Shout;

public record ShoutInput
{
    public required string Text { get; init; }
}

public record ShoutOutput(string Text);
```

```csharp
// Trains/Shout/Junctions/UppercaseJunction.cs
using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Hub.Trains.Shout.Junctions;

public class UppercaseJunction : EffectJunction<ShoutInput, ShoutOutput>
{
    public override Task<ShoutOutput> Run(ShoutInput input) =>
        Task.FromResult(new ShoutOutput(input.Text.ToUpperInvariant()));
}
```

```csharp
// Trains/Shout/IShoutTrain.cs and ShoutTrain.cs
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Hub.Trains.Shout.Junctions;

namespace Trax.Samples.Hub.Trains.Shout;

public interface IShoutTrain : IServiceTrain<ShoutInput, ShoutOutput>;

[TraxAuthorize(Roles = "User")]
[TraxQuery(Description = "Shouts the text back")]
public class ShoutTrain : ServiceTrain<ShoutInput, ShoutOutput>, IShoutTrain
{
    protected override Task<Either<Exception, ShoutOutput>> Junctions() =>
        Chain<UppercaseJunction>().Resolve();
}
```

Restart, and `query { discover { shout(input: { text: "hi" }) { text } } }` answers `HI`. The host
refuses to start if an exposed train has neither `[TraxAuthorize]` nor `[TraxAllowAnonymous]`, or
if a junction needs an input nothing before it produces.

To schedule a train, its input implements `IManifestProperties` (as `HelloWorldInput` does), and
`AddScheduler` in `Program.cs` gets another `.Schedule<IYourTrain>("your-id", input, Every.Minutes(5))`
or `Cron.Daily()`. See [Scheduling](https://traxsharp.net/docs/scheduler).

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

Trax creates and migrates its `trax` schema at startup. With Postgres the scheduler queues work in
the database and runs it on local workers, so `[TraxMutation]` without `GraphQLOperation.Run` (which
adds `mode: QUEUE`) works too, and several processes can share one database. See
[Data Persistence](https://traxsharp.net/docs/effect/effect-providers/data-persistence).

## Before deploying

Two things exist only in Development: the demo key and the dashboard. Started any other way
(`dotnet bin/.../Trax.Samples.Hub.dll`, a container), the application runs in Production, every
operation is refused and `/trax` is a 404.

- Replace the demo key with real credentials (`keys.AddHashed(...)` from a secret store, or
  `AddTraxJwtAuth`), then remove the `IsDevelopment()` check around `AddTraxApiKeyAuth`. See
  [API Security](https://traxsharp.net/docs/api-security).
- Choose who may use the dashboard, `RequirePolicy("<policy>")` or `RequireRoles("<role>")`, in
  place of `AllowAnonymousDashboard()`, then remove the two `IsDevelopment()` checks around it. See
  [Dashboard](https://traxsharp.net/docs/dashboard).
- Outside `dotnet run` the URL comes from `ASPNETCORE_URLS` (or `ASPNETCORE_HTTP_PORTS`), not
  `launchSettings.json`.

## Where to go next

- [Project Templates](https://traxsharp.net/docs/reference/templates): every file this template generated
- [Getting Started](https://traxsharp.net/docs/getting-started): the same server built step by step from an empty folder
- [Registration Order](https://traxsharp.net/docs/reference/registration-order): what must come before what, and the startup errors
- [API](https://traxsharp.net/docs/api): queries, mutations, subscriptions
- [Samples](https://traxsharp.net/docs/samples): complete applications, one per feature
