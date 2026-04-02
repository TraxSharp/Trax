---
layout: default
title: E2E Testing
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
        builder.UseSetting("ConnectionStrings:TraxDatabase", TestConnectionString);

        builder.ConfigureTestServices(services =>
        {
            services.AddHostedService<ConfigureSchedulerForTestsService>();
        });
    }
}
```

Both the Scheduler and API entry points need a `WebApplicationFactory<T>` marker class appended after `app.Run()`:

```csharp
// At the end of Program.cs, after app.Run();
namespace MyApp.Scheduler
{
    public partial class Program;
}
```

### Test Database

E2E tests run against a real Postgres database. Add a dedicated database to your `docker-compose.yml`:

```yaml
environment:
  POSTGRES_MULTIPLE_DATABASES: my_app,my_app_e2e_tests
```

This isolates test data from development data and prevents cross-contamination.

## Scheduler Configuration for Tests

The scheduler's `SchedulerConfiguration` is a mutable singleton. Override polling intervals and limits via a hosted service registered in `ConfigureTestServices`:

```csharp
private sealed class ConfigureSchedulerForTestsService(SchedulerConfiguration config)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        config.ManifestManagerPollingInterval = TimeSpan.FromSeconds(1);
        config.JobDispatcherPollingInterval = TimeSpan.FromSeconds(1);
        config.DefaultRetryDelay = TimeSpan.FromSeconds(2);
        config.DefaultJobTimeout = TimeSpan.FromSeconds(30);
        config.MaxActiveJobs = 100;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

Fast polling (1s) keeps tests responsive. Reduced retry delays prevent slow dead-letter tests. High `MaxActiveJobs` avoids capacity-related dispatch blocking.

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

Send GraphQL queries as JSON POST requests to `/trax/graphql`. Remember that HotChocolate returns HTTP 400 for GraphQL errors, so read the response body regardless of status code.

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
| EF graph traversal doesn't cascade UPDATEs | E2E test |

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [AddScheduler](/docs/sdk-reference/scheduler/scheduler-configuration-builder) | [AddMediator](/docs/sdk-reference/mediator-api/add-service-train-bus) | [RunAsync](/docs/sdk-reference/mediator-api/train-bus)
