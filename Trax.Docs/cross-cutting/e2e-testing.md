---
layout: default
title: E2E Testing
description: "End-to-end testing a Trax host with WebApplicationFactory: test database, scheduler settings, cron retries, reading Trax tables, GraphQL and subscriptions."
parent: Cross-Cutting
nav_order: 4
---

# E2E Testing

E2E tests verify that the full Trax application works correctly: scheduler dispatches work, trains execute, dependencies chain, failures dead-letter, and GraphQL resolves against real infrastructure. They complement [unit and integration tests](/docs/cross-cutting/testing) by catching issues that only surface when all components run together (DI wiring, EF graph traversal, scheduler timing, authorization).

## Architecture

E2E tests use `WebApplicationFactory<T>` to host your ASP.NET Core app in-process. The factory starts the real host with all hosted services (scheduler polling, local workers, manifest management) but overrides the connection string to point at a dedicated test database.

```csharp
public class MySchedulerFactory : WebApplicationFactory<Scheduler.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:TraxDatabase", TestConnectionString);
    }
}
```

On .NET 10 the `Program` class that top-level statements generate is public, so a test project
that references one host writes `WebApplicationFactory<Program>` with no marker. A test project
that references **two** hosts (a scheduler and an API, say) sees two global `Program` types and
cannot name either. Give each host a marker in its own namespace, after `app.Run()`:

```csharp
// At the end of Program.cs, after app.Run();
namespace MyApp.Scheduler
{
    public partial class Program;
}
```

`MyApp.Scheduler.Program` is a separate type from the generated entry point, and that is fine:
`WebApplicationFactory<T>` uses `T` only to find the host's assembly. The samples do this
(`samples/Scheduling/Trax.Samples.Scheduling.Host/Program.cs` in
[Trax.Samples](https://github.com/TraxSharp/Trax.Samples)), so the factory reads
`WebApplicationFactory<Host.Program>`.

### Test Database

E2E tests run against a real Postgres database. Add a dedicated database to your `docker-compose.yml`:

```yaml
environment:
  POSTGRES_MULTIPLE_DATABASES: my_app,my_app_e2e_tests
```

This isolates test data from development data and prevents cross-contamination.

Start each suite on an empty `trax` schema, so every run sees the host's first start (migrations,
manifest seeding) and nothing a previous run left. Drop it, and your domain schemas, before the
factory starts the host, and clear Npgsql's pools when the suite ends:

```csharp
[SetUpFixture]
public class SuiteSetup
{
    public static MyHostFactory Factory { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        await using (var connection = new NpgsqlConnection(MyHostFactory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand(
                "DROP SCHEMA IF EXISTS trax CASCADE; DROP SCHEMA IF EXISTS shop CASCADE;", connection);
            await drop.ExecuteNonQueryAsync();
        }

        Factory = new MyHostFactory();
        _ = Factory.Services; // starts the host: Trax migrations, your schema, manifest seeding
    }

    [OneTimeTearDown]
    public async Task StopHost()
    {
        await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }
}
```

`tests/Trax.Samples.Scheduling.E2E/Fixtures/SharedSchedulingSetup.cs` is the same setup, waiting
for the seeded manifests before any test runs.

## Two ways to drive the scheduler

| | Keep the scheduler running | Disable the ManifestManager, insert work queue rows |
|---|---|---|
| What runs | The host exactly as in production: the ManifestManager queues manifest runs and retries, the JobDispatcher dispatches them | Only the JobDispatcher. Work exists only when a test writes a `WorkQueue` row |
| How a test acts | Through the public surface: GraphQL mutations, `ITrainBus`, `ITraxScheduler.TriggerAsync`, a fake dependency it flips (a feed that starts failing) | `WorkQueue.Create` plus `SaveChanges`, with the input it wants |
| How a test isolates itself | Marks the newest `metadata.id` before it acts and reads only rows after it; nothing is cleaned between tests | Cleans execution tables in `[SetUp]`, keeps manifests |
| Use it for | Behaviour a user or operator sees: retries and backoff, dead letters and requeue, dependents, authorization, subscriptions | Dispatch of a specific input in isolation: dormant dependents activated inside a scheduled run, subject serialization, a deferred (staged) entry |

Start with the first. It tests what ships, and it is what the
[Scheduling sample](/docs/samples/scheduling) does (`tests/Trax.Samples.Scheduling.E2E`, whose
`SchedulingTestFixture` marks the latest metadata id). Reach for the second only when the
ManifestManager's own timing gets in the way of what the test asserts.

## Scheduler Configuration for Tests

Production settings (a 5-minute `DefaultRetryDelay`, a nightly cron) make a test wait for hours.
Make them configuration in `Program.cs`, with the production value as the default, and override
them from the factory with `UseSetting`:

```csharp
// Program.cs
var retryDelay = builder.Configuration.GetValue<TimeSpan?>("Shop:RetryDelay") ?? TimeSpan.FromMinutes(5);
var pollInterval = builder.Configuration.GetValue<TimeSpan?>("Shop:PollingInterval") ?? TimeSpan.FromSeconds(5);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler(scheduler => scheduler
        .ManifestManagerPollingInterval(pollInterval)
        .JobDispatcherPollingInterval(pollInterval)
        .DefaultRetryDelay(retryDelay)));
```

```csharp
// The test factory
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.UseEnvironment("Development");
    builder.UseSetting("ConnectionStrings:TraxDatabase", ConnectionString);
    builder.UseSetting("Shop:RetryDelay", "00:00:01");
    builder.UseSetting("Shop:PollingInterval", "00:00:01");
}
```

The Scheduling sample takes the other route: its Development settings in `Program.cs` are already
demo speed (1-second polling, 2-second retry delay), and its factory changes only the connection
string.

`SchedulerConfiguration` is also a singleton whose `ManifestManagerEnabled`, polling intervals,
`DefaultRetryDelay`, `DefaultMaxRetries`, `DefaultJobTimeout` and `MaxActiveJobs` can be set at
runtime (their setters are hidden from IntelliSense but public), for example from a hosted service
registered in `ConfigureTestServices`. It holds scheduler-wide defaults only: it cannot change a
manifest's schedule, or a retry count or delay the manifest set for itself. For those, use
configuration as above.

## Testing a cron manifest's retries

A failed run is retried when its manifest is next due ([When a retry runs](/docs/scheduler/dead-letters-and-cleanup#when-a-retry-runs)).
A run you start with `TriggerAsync` or `triggerManifest` while the cron is not due is retried only
at the cron's next occurrence, so triggering a nightly job in a test and waiting for its retries
waits until tomorrow.

Test the two halves separately:

- **The retry and dead-letter behaviour.** Make the schedule configuration, and give the test host
  a short interval. Retries, backoff, `MaxRetries` and the dead letter do not depend on whether the
  schedule is a cron or an interval:

  ```csharp
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
  without running it. `tests/Trax.Samples.Scheduling.E2E/SchedulingTests/CronScheduleTests.cs`
  does this for a daily 07:00 UTC cron.

`SupplierOutage.cs` in the same folder's `Utilities` drives a manifest to its dead letter and
requeues it, and `RetryAndDeadLetterTests.cs` asserts the backoff between the failed runs.

## Test Fixture Pattern

The base fixture handles lifecycle: create the factory once per class, seed manifests, then clean execution data between tests while preserving manifests.

```csharp
[TestFixture]
public abstract class SchedulerTestFixture
{
    private MySchedulerFactory Factory { get; set; } = null!;
    protected ITrainBus TrainBus { get; private set; } = null!;
    protected IDataContext DataContext { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        Factory = new MySchedulerFactory();
        _ = Factory.Services; // triggers host startup
        await WaitForManifestsSeeded();

        // Disable ManifestManager to prevent automatic scheduling
        // that competes with test-created entries.
        var config = Factory.Services.GetRequiredService<SchedulerConfiguration>();
        config.ManifestManagerEnabled = false;
    }

    [SetUp]
    public virtual async Task SetUp()
    {
        // Create per-test scope and clean execution data
        // (metadata, work queues, dead letters, logs, background jobs).
        // Preserve manifests. They don't need re-seeding.
    }
}
```

The fixture's `IDataContext` comes from the host's `IDataContextProviderFactory`. Create one per
read, in a scope, and dispose it, so each read sees what the scheduler committed rather than an
entity a long-lived context cached:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;

public sealed class Db(IServiceProvider services)
{
    public async Task<T> Query<T>(Func<IDataContext, Task<T>> query, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var dataContext = await factory.CreateDbContextAsync(ct);
        try
        {
            return await query(dataContext);
        }
        finally
        {
            (dataContext as IDisposable)?.Dispose();
        }
    }
}

// var runs = await db.Query(dc => dc.Metadatas.AsNoTracking().Where(m => m.ExternalId == id).ToListAsync());
```

Pass `Factory.Services`. `tests/Trax.Samples.Scheduling.E2E/Utilities/Db.cs` is this class with
waits for runs and dead letters built on it.

Disabling the ManifestManager after startup prevents automatic work queue creation that would interfere with manually-created test entries. The JobDispatcher stays enabled to dispatch those entries.

For tests that need the ManifestManager (dependency chains, dead-letter verification), re-enable it in a `try/finally` block:

```csharp
EnableManifestManager();
try
{
    // ... test that needs ManifestManager
}
finally
{
    DisableManifestManager();
}
```

## Creating Work Queue Entries

When testing scheduler dispatch (as opposed to `TrainBus.RunAsync`), create work queue entries manually. This is required for dormant dependent tests where `IDormantDependentContext.ActivateAsync` only works within a scheduled execution context.

**Important:** Serialize input using `TraxJsonSerializationOptions.ManifestProperties`. This uses camelCase property naming, matching what the scheduler's `DispatchJobsJunction` expects during deserialization. Using default `JsonSerializer.Serialize` produces PascalCase, which causes silent deserialization failures.

```csharp
var entry = WorkQueue.Create(new CreateWorkQueue
{
    TrainName = manifest.Name,
    Input = JsonSerializer.Serialize(
        input,
        TraxJsonSerializationOptions.ManifestProperties  // camelCase, must match dispatcher
    ),
    InputTypeName = typeof(MyInput).FullName,
    ManifestId = manifest.Id,
    Priority = 20,
});

await DataContext.Track(entry);
await DataContext.SaveChanges(CancellationToken.None);
DataContext.Reset();
```

Build the entry with `WorkQueue.Create`; it is the only way to build one, because `WorkQueue`'s parameterless constructor is protected. `Create` stamps `ConfirmedAt` unless you ask it to defer. An entry with a null `ConfirmedAt` is a staged entry: the dispatcher never claims it, and once it is older than `StaleStagedEntryTimeout` the ManifestManager's sweep cancels it.

An entry built this way skips everything `ITrainExecutionService.QueueAsync` does: authorization, the `OnQueue` hook and `QueueSubjectKey`. `CreateWorkQueue` has three more fields for tests that need them:

| Field | Default | Effect |
|-------|---------|--------|
| `SubjectKey` | `null` | The [subject](/docs/core/trains-and-junctions#queuesubjectkey-serializing-work-that-touches-the-same-thing) the entry is serialized against. Entries sharing a non-null key are not dispatched concurrently. `Create` throws `ArgumentException` for an empty or whitespace-only key, one holding a NUL character or an unpaired surrogate, or one longer than 512 characters. Characters are counted as Unicode characters, so an emoji counts once although it is two UTF-16 units |
| `DeferPromotion` | `false` | Commits the entry unconfirmed, so the dispatcher will not claim it until [`IWorkQueuePromotion`](/docs/sdk-reference/scheduler-api/i-work-queue-promotion) promotes it |
| `ExplicitTrigger` | `false` | Marks the entry as a run someone asked for by name, so it is dispatched while its manifest is disabled. A scheduled entry of a disabled manifest waits until the manifest is enabled. `Create` sets it for any entry with a `DeadLetterId` |

## Polling for State

Use a poller utility to wait for metadata or dead letters to reach expected states. Poll every 250ms with `AsNoTracking()` and `dataContext.Reset()` between polls to get fresh data from the database:

```csharp
public static async Task<Metadata> WaitForMetadataByManifestId(
    IDataContext dataContext,
    long manifestId,
    TrainState expectedState,
    TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;

    while (DateTime.UtcNow < deadline)
    {
        dataContext.Reset();
        var metadata = await dataContext.Metadatas
            .AsNoTracking()
            .Where(m => m.ManifestId == manifestId)
            .FirstOrDefaultAsync(m => m.TrainState == expectedState);

        if (metadata != null)
            return metadata;

        await Task.Delay(TimeSpan.FromMilliseconds(250));
    }

    throw new TimeoutException(...);
}
```

For negative assertions (verifying something does NOT happen), poll for a short duration and assert no matching records appear.

## API E2E Tests

For GraphQL API tests, create a separate `WebApplicationFactory<Api.Program>` and use `HttpClient` to send requests:

```csharp
[TestFixture]
public abstract class ApiTestFixture
{
    private WebApplicationFactory<Api.Program> Factory { get; set; } = null!;
    protected HttpClient HttpClient { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Factory = new MyApiFactory();
        HttpClient = Factory.CreateClient();
    }
}
```

A host that serves both the scheduler and the API needs only one factory, and its `HttpClient`.

Send GraphQL queries as JSON POST requests to `/trax/graphql`, with the credential in the header
the auth scheme reads (`X-Api-Key` for API keys). The HTTP status depends on where the request
failed, so read the body regardless of status:

| Failure | HTTP status | Body |
|---|---|---|
| The document does not validate: an unknown field, a missing or misnamed input field | `400` | `errors`, no `data` |
| A field refused while executing, such as `TRAX_AUTHORIZATION` (`"Not authorized."`) on a train or the operations namespace | `200` | `errors` with `extensions.code`, and `data` with the refused field `null` |

So assert on `errors[0].extensions.code`, not on the status. A small client that returns the parsed
body and the status together is `tests/Trax.Samples.Scheduling.E2E/Utilities/GraphQLClient.cs`.

## Testing subscriptions

`WebApplicationFactory` serves WebSockets through its test server. Open a socket with
`Factory.Server.CreateWebSocketClient()`, ask for the `graphql-transport-ws` subprotocol, send
`connection_init` with the credential in its payload (`apiKey`, `authToken` or `bearer`, not the
HTTP header's name), wait for `connection_ack`, then send `subscribe` and read `next` messages:

```csharp
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
([Watching a queued run](/docs/sdk-reference/graphql-api/subscriptions#watching-a-queued-run)), give
every receive a timeout, and expect a refused subscription as a message of type `error` whose payload
holds `TRAX_AUTHORIZATION`, not as a closed socket. The subscriber receives a train's events only if
the train is `[TraxBroadcast]` and admits it, or the key satisfies the operations gate.

`tests/Trax.Samples.ChatService.E2E/Utilities/GraphQLWebSocketClient.cs` is a complete client
(connect, subscribe, receive with a timeout, the close code of a refused connection), and
`ChatApiTests/LifecycleSubscriptionTests.cs` next to it uses it.

## Test Parallelism

All E2E tests share one database. Add `[assembly: NonParallelizable]` to prevent NUnit from running fixtures concurrently. Each fixture creates its own `WebApplicationFactory` with its own scheduler instance, so concurrent execution would cause contention on shared database resources.

## When to Use E2E vs Other Test Types

| Scenario | Test Type |
|----------|-----------|
| Junction logic in isolation | Unit test with fakes |
| Train orchestration with InMemory | Integration test |
| Scheduler dispatches work correctly | E2E test |
| Dependency chains trigger correctly | E2E test |
| Dormant dependents activate on condition | E2E test |
| Failures dead-letter after retries | E2E test |
| GraphQL authorization enforcement | E2E test |
| Subscription delivery and refusal | E2E test |
| EF graph traversal doesn't cascade UPDATEs | E2E test |

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [RunAsync](/docs/sdk-reference/mediator-api/train-bus) | [IWorkQueuePromotion](/docs/sdk-reference/scheduler-api/i-work-queue-promotion)
