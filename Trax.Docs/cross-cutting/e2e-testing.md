---
title: Test a host end to end
description: "Start a Trax host in a test with WebApplicationFactory on its own Postgres database, drive the scheduler, read Trax tables and call GraphQL."
type: how-to
area: testing
nav_order: 3
---

# Test a host end to end

An end-to-end test starts the whole application in the test process, `Program.cs` included, and
drives it through what it exposes: HTTP, GraphQL, the train bus, the scheduler. It is the test
that fails when the host no longer starts, when something meant for Development reaches
Production, or when scheduling, retries, dependents and authorization stop working together. For
a train on its own, see [Test a train with in-memory effects](/docs/cross-cutting/test-a-train).

The test project references the host project and `Microsoft.AspNetCore.Mvc.Testing`, as in
[Unit-test a junction](/docs/cross-cutting/testing#set-up-the-test-project).

## Start the host in a test

`WebApplicationFactory<Program>` starts the host in memory with all its hosted services. The
top-level `Program` class is public in .NET 10, so `Program.cs` needs nothing added. Pick the
environment with `UseEnvironment` and call the host with the factory's `HttpClient`:

```csharp from=Trax.Samples/templates/content/Trax.Samples.Hub/tests/Trax.Samples.Hub.Tests/IntegrationTests/HostTests.cs
    private static WebApplicationFactory<Program> Start(string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseEnvironment(environment)
        );
// ...
    [Test]
    public async Task Production_Dashboard_IsNotServed()
    {
        await using var app = Start("Production");

        var response = await app.CreateClient().GetAsync("/trax/dashboard/");

        Assert.That(
            response.StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound),
            "the dashboard has no authorization in front of it, so it exists only in Development"
        );
    }
```

The `trax-hub` template's `tests/<Name>.Tests/IntegrationTests/HostTests.cs` is the complete set:
the host starts in both environments, the dashboard is served only in Development, and the demo
API key works only in Development.

## Name each host when the project tests two

A test project that references two hosts (a scheduler and an API, say) sees two global `Program`
types and can name neither. Give each host a marker in its own namespace, after `app.Run()`:

```csharp from=Trax.Samples/samples/Scheduling/Trax.Samples.Scheduling.Host/Program.cs
app.Run();

namespace Trax.Samples.Scheduling.Host
{
    public partial class Program;
}
```

`WebApplicationFactory<T>` uses `T` only to find the host's assembly, so the marker, a separate
type from the generated entry point, works: the Scheduling sample's tests use
`WebApplicationFactory<Host.Program>`.

## Give the suite its own database

Run the suite against a Postgres database that holds nothing else, so a test run never touches
development data. With the samples' compose file, list it beside the application's database:

```yaml
environment:
  POSTGRES_MULTIPLE_DATABASES: my_app,my_app_e2e_tests
```

Point the host at it from the factory by overriding the connection string setting the host reads:

```csharp from=Trax.Samples/lib/Trax.Samples.Shared.Testing/SampleApiFactory.cs
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseSetting("ConnectionStrings:TraxDatabase", ConnectionString);
    }
```

Start each suite on an empty `trax` schema, so every run sees the host's first start (migrations,
manifest seeding) and nothing a previous run left. Drop it, and the application's own schemas,
before the factory starts the host, and clear Npgsql's pools when the suite ends:

```csharp from=Trax.Samples/tests/Trax.Samples.Scheduling.E2E/Fixtures/SharedSchedulingSetup.cs
    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await DropTraxSchema();

        StartedAt = DateTime.UtcNow;
        Factory = new SchedulingHostFactory();

        // Resolving Services starts the host: migrations run and the manifests are seeded
        // before the polling services start.
        Db = new Db(Factory.Services);
// ...
    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }

    private static async Task DropTraxSchema()
    {
        await using var connection = new NpgsqlConnection(
            SchedulingHostFactory.TestConnectionString
        );
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DROP SCHEMA IF EXISTS trax CASCADE;",
            connection
        );
        await command.ExecuteNonQueryAsync();
    }
```

One host serves the suite and the tests share its database, so run them one at a time:

```csharp from=Trax.Samples/tests/Trax.Samples.Scheduling.E2E/GlobalUsings.cs
[assembly: NonParallelizable]
```

## Read the Trax tables

Read through the host's `IDataContextProviderFactory`, creating one data context per read in a
scope of its own, so each read sees what the scheduler committed rather than an entity a
long-lived context cached:

```csharp from=Trax.Samples/tests/Trax.Samples.Scheduling.E2E/Utilities/Db.cs
public sealed class Db(IServiceProvider services)
{
// ...
    public async Task<T> Query<T>(
        Func<IDataContext, Task<T>> query,
        CancellationToken cancellationToken = default
    )
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var dataContext = await factory.CreateDbContextAsync(cancellationToken);
        try
        {
            return await query(dataContext);
        }
        finally
        {
            (dataContext as IDisposable)?.Dispose();
        }
    }
```

Pass it `Factory.Services`. The sample's `Db.cs` builds its waits on this: for runs of a
manifest to reach a state, and for a dead letter. Each wait polls with a timeout and fails the
test with what it saw instead.

## Choose how the scheduler runs under test

| | Keep the scheduler running | Disable the ManifestManager, insert work queue rows |
|---|---|---|
| What runs | The host as in production: the ManifestManager queues manifest runs and retries, the JobDispatcher dispatches them | Only the JobDispatcher. Work exists only when a test writes a `WorkQueue` row |
| How a test acts | Through the public surface: GraphQL mutations, `ITrainBus`, `ITraxScheduler.TriggerAsync`, a fake dependency it flips (a feed that starts failing) | `WorkQueue.Create` and `SaveChanges`, with the input it wants |
| How a test isolates itself | Marks the newest `metadata.id` before it acts and reads only rows after it; nothing is cleaned between tests | Deletes execution rows in `[SetUp]` and keeps the manifests |
| Fits | Behaviour a user or operator sees: retries and backoff, dead letters and requeue, dependents, authorization, subscriptions | Dispatch of one specific input in isolation |

Keeping the scheduler running tests what ships, and is what the
[Scheduling sample](/docs/samples/scheduling)'s suite does. Disable the ManifestManager only when
its own timing gets in the way of what a test asserts; the EnergyHub sample's suite does.

### Keep the scheduler running

Before acting, a test takes the newest metadata id as its marker, then waits only for runs after
it:

```csharp from=Trax.Samples/tests/Trax.Samples.Scheduling.E2E/SchedulingTests/DormantDependentTests.cs
    [Test]
    public async Task Parent_activates_the_dormant_dependent_with_its_own_input()
    {
        var alert = await Db.Manifest(ManifestNames.AlertRateSpike);
        var marker = await Db.LatestMetadataId();

        RateFeed.SpikeNext();

        var run = await Db.WaitForRun(
            alert.Id,
            TrainState.Completed,
            marker,
            TimeSpan.FromSeconds(30)
        );
```

`RateFeed` is a fake the host registers, which the test resolves from `Factory.Services` and
flips to make the next parent run report a spike.

### Disable the ManifestManager and insert work queue rows

`SchedulerConfiguration` is a singleton, and `ManifestManagerEnabled` can be set at runtime (its
setter is public but hidden from IntelliSense). Wait for the manifests to be seeded, then turn it
off so it queues nothing the test did not write:

```csharp from=Trax.Samples/tests/Trax.Samples.EnergyHub.E2E/Fixtures/SharedHubSetup.cs
        await WaitForManifestsSeeded();

        var config = Factory.Services.GetRequiredService<SchedulerConfiguration>();
        config.ManifestManagerEnabled = false;
```

Clean the execution rows before each test and keep the manifests:

```csharp from=Trax.Samples/tests/Trax.Samples.EnergyHub.E2E/Fixtures/HubTestFixture.cs
    private async Task CleanExecutionData()
    {
        await DataContext.BackgroundJobs.ExecuteDeleteAsync();
        await DataContext.Logs.ExecuteDeleteAsync();
        await DataContext.WorkQueues.ExecuteDeleteAsync();
        await DataContext.DeadLetters.ExecuteDeleteAsync();
        await DataContext.Metadatas.ExecuteDeleteAsync();
        DataContext.Reset();
    }
```

A test then writes the entry the dispatcher claims. Serialize the input with
`TraxJsonSerializationOptions.ManifestProperties`, the camelCase options the dispatcher reads it
back with; `JsonSerializer`'s defaults write PascalCase, which the dispatcher does not bind:

```csharp from=Trax.Samples/tests/Trax.Samples.EnergyHub.E2E/HubTests/DependencyChainTests.cs
            var entry = Trax.Effect.Models.WorkQueue.WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = solarManifest.Name,
                    Input = JsonSerializer.Serialize(
                        input,
                        TraxJsonSerializationOptions.ManifestProperties
                    ),
                    InputTypeName = typeof(MonitorSolarProductionInput).FullName,
                    ManifestId = solarManifest.Id,
                    Priority = 20,
                }
            );

            await DataContext.Track(entry);
            await DataContext.SaveChanges(CancellationToken.None);
            DataContext.Reset();
```

`WorkQueue.Create` is the only way to build an entry; the parameterless constructor is protected.
It stamps `ConfirmedAt` unless the entry is deferred. An entry with a null `ConfirmedAt` is staged:
the dispatcher never claims it, and once it is older than `StaleStagedEntryTimeout` the
ManifestManager's sweep cancels it. An entry built this way skips everything
`ITrainExecutionService.QueueAsync` does: authorization, the `OnQueue` hook and `QueueSubjectKey` (see [Enqueue hooks](/docs/mediator/enqueue-hooks)).
`CreateWorkQueue` has three more fields for tests that need them:

| Field | Default | Effect |
|-------|---------|--------|
| `SubjectKey` | `null` | The subject the entry is serialized against: entries sharing a non-null key are not dispatched concurrently. `Create` throws `ArgumentException` for an empty or whitespace-only key, one holding a NUL character or an unpaired surrogate, or one longer than 512 characters (counted as Unicode characters, so an emoji counts once) |
| `DeferPromotion` | `false` | Commits the entry unconfirmed, so the dispatcher does not claim it until [`IWorkQueuePromotion`](/docs/sdk-reference/scheduler-api/i-work-queue-promotion) promotes it |
| `ExplicitTrigger` | `false` | Marks the entry as a run someone asked for by name, so it is dispatched while its manifest is disabled. A scheduled entry of a disabled manifest waits until the manifest is enabled. `Create` sets it for any entry with a `DeadLetterId` |

A test that needs the ManifestManager (a dependent queued after its parent completes) turns it
back on around the part that needs it, in a `try`/`finally`, as `DependencyChainTests` does with
the fixture's `EnableManifestManager()` and `DisableManifestManager()`.

## Make scheduler settings configurable

Production settings (a 5-minute `DefaultRetryDelay`, a nightly cron) make a test wait for hours.
Read them from configuration in `Program.cs`, with the production value as the default:

```csharp compile
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

var retryDelay =
    builder.Configuration.GetValue<TimeSpan?>("Shop:RetryDelay") ?? TimeSpan.FromMinutes(5);
var pollInterval =
    builder.Configuration.GetValue<TimeSpan?>("Shop:PollingInterval") ?? TimeSpan.FromSeconds(5);

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(Program).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                .ManifestManagerPollingInterval(pollInterval)
                .JobDispatcherPollingInterval(pollInterval)
                .DefaultRetryDelay(retryDelay)
        )
);
```

and override them from the factory:

```csharp fragment
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.UseEnvironment("Development");
    builder.UseSetting("ConnectionStrings:TraxDatabase", ConnectionString);
    builder.UseSetting("Shop:RetryDelay", "00:00:01");
    builder.UseSetting("Shop:PollingInterval", "00:00:01");
}
```

The Scheduling sample's `Program.cs` uses demo-speed settings already (1-second polling, a
2-second retry delay), so its factory changes only the connection string.

`SchedulerConfiguration`'s polling intervals, `DefaultRetryDelay`, `DefaultMaxRetries`,
`DefaultJobTimeout` and `MaxActiveJobs` can also be set at runtime, like `ManifestManagerEnabled`.
They are scheduler-wide defaults: they do not change a manifest's schedule, or a retry count or
delay the manifest sets for itself. For those, use configuration as above.

## Test a cron manifest's retries

A failed run is retried when its manifest is next due
([When a retry runs](/docs/scheduler/how-retries-work#when-a-retry-runs)). A run started
with `TriggerAsync` or `triggerManifest` while the cron is not due is retried only at the cron's
next occurrence, so triggering a nightly job in a test and waiting for its retries waits until
tomorrow. Test the two halves separately:

- **The retry and dead-letter behaviour.** Make the schedule configuration, and give the test host
  a short interval. Retries, backoff, `MaxRetries` and the dead letter do not depend on whether the
  schedule is a cron or an interval:

  ```csharp fragment
  // Program.cs: nightly at 02:00 UTC, every few seconds under test
  var everySeconds = builder.Configuration.GetValue<int?>("Shop:ResendEverySeconds");
  Schedule resendSchedule = everySeconds is { } seconds ? Every.Seconds(seconds) : Cron.Daily(hour: 2);

  scheduler.Schedule<IResendUnsentInvoicesTrain>(
      "resend-unsent-invoices", new ResendUnsentInvoicesInput(), resendSchedule,
      options => options.MaxRetries(3));
  ```

  With `UseSetting("Shop:ResendEverySeconds", "2")` and a 1-second retry delay, a resend that keeps
  failing runs four times and is dead-lettered within about 20 seconds.
- **The cron itself.** Assert the manifest's `NextScheduledRun` against the occurrence you expect,
  without running it. The Scheduling sample's `SchedulingTests/CronScheduleTests.cs` does this for
  a daily 07:00 UTC cron.

The sample's `Utilities/SupplierOutage.cs` drives a manifest to its dead letter and requeues it,
and `SchedulingTests/RetryAndDeadLetterTests.cs` asserts the backoff between the failed runs.

## Send GraphQL requests

Post queries as JSON to `/trax/graphql`, with the credential in the header the auth scheme reads
(`X-Api-Key` for API keys). The HTTP status depends on where the request failed, so read the body
regardless of status:

| Failure | HTTP status | Body |
|---|---|---|
| The document does not validate: an unknown field, a missing or misnamed input field | `400` | `errors`, no `data` |
| A field refused while executing, such as `TRAX_AUTHORIZATION` (`"Not authorized."`) on a train or the operations namespace | `200` | `errors` with `extensions.code`, and `data` with the refused field `null` |

Assert on `errors[0].extensions.code`, not on the status. The Scheduling sample's
`Utilities/GraphQLClient.cs` is a small client that returns the parsed body and the status
together. A host that serves both the scheduler and the API needs one factory and its
`HttpClient`.

## Testing subscriptions

`WebApplicationFactory` serves WebSockets through its test server. Open a socket with
`Factory.Server.CreateWebSocketClient()`, ask for the `graphql-transport-ws` subprotocol, send
`connection_init` with the credential in its payload (`apiKey`, `authToken` or `bearer`, not the
HTTP header's name), wait for `connection_ack`, then send `subscribe` and read `next` messages:

```csharp fragment
var ws = Factory.Server.CreateWebSocketClient();
ws.ConfigureRequest = request => request.Headers["Sec-WebSocket-Protocol"] = "graphql-transport-ws";
var socket = await ws.ConnectAsync(new Uri("ws://localhost/trax/graphql"), CancellationToken.None);

await Send(socket, new { type = "connection_init", payload = new { apiKey = BillingKey } });
// expect { "type": "connection_ack" }
await Send(socket, new { id = "1", type = "subscribe",
    payload = new { query = "subscription { onTrainStateChanged { externalId trainState output } }" } });
// then send the mutation over HTTP and read { "type": "next", "payload": { "data": ... } }
```

Subscribe before you trigger the run: there is no replay, and `graphql-transport-ws` sends no
acknowledgement of a `subscribe`. Filter events by `externalId`
([Watching a queued run](/docs/api/watch-a-queued-run)), give
every receive a timeout, and expect a refused subscription as a message of type `error` whose payload
holds `TRAX_AUTHORIZATION`, not as a closed socket. The subscriber receives a train's events only if
the train is `[TraxBroadcast]` and admits it, or the key satisfies the operations gate.

The ChatService sample's `tests/Trax.Samples.ChatService.E2E/Utilities/GraphQLWebSocketClient.cs`
is a complete client (connect, subscribe, receive with a timeout, the close code of a refused
connection), and `ChatApiTests/LifecycleSubscriptionTests.cs` next to it uses it.

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [RunAsync](/docs/sdk-reference/mediator-api/i-train-bus) | [IWorkQueuePromotion](/docs/sdk-reference/scheduler-api/i-work-queue-promotion)
