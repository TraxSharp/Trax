---
title: Test a train with in-memory effects
description: "Run a train in a test through ITrainBus on a container with the in-memory data provider, assert its output or exception, and read the run's Metadata row."
type: how-to
area: testing
nav_order: 2
source:
  - Trax.Effect/src/Trax.Effect.Data.InMemory/Extensions/ServiceExtensions.cs
---

# Test a train with in-memory effects

A train test builds a container with the same Trax registration as `Program.cs`, swaps the data
provider for `UseInMemory()` and the application's outside dependencies for fakes, and runs the
train through `ITrainBus` the way the application does. The junctions run for real. For a test
of one junction on its own, see [Unit-test a junction](/docs/cross-cutting/testing).

## Build the container

Register logging, the fakes, and Trax with the in-memory provider (package
`Trax.Effect.Data.InMemory`). `AddMediator` takes the assembly that holds the trains, as in
`Program.cs`:

```csharp compile
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<IUserRepository, FakeUserRepository>();
services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UseInMemory())
        .AddMediator(typeof(CreateUserTrain).Assembly)
);

await using var provider = services.BuildServiceProvider();
var bus = provider.GetRequiredService<ITrainBus>();

var user = await bus.RunAsync<User>(new CreateUserRequest { Email = "test@example.com" });
```

In an NUnit fixture, build the container in `[SetUp]` and dispose it in `[TearDown]`, so each test
starts on an empty store: every `UseInMemory()` call creates its own, which every scope of that
container shares.

## Assert the output or the exception

`RunAsync<TOut>` returns the train's output. When a junction throws, the train stops and
`RunAsync` throws that exception, so a failing run is asserted with `Assert.ThrowsAsync`:

```csharp fragment
Assert.ThrowsAsync<ValidationException>(
    () => bus.RunAsync<User>(new CreateUserRequest { Email = "taken@example.com" })
);
```

## Read the run's record

Every run writes a `Metadata` row, which a test reads through the scoped `IDataContext`.
`Metadata.Name` is the full name of the train's interface. With
[`SaveTrainParameters()`](/docs/sdk-reference/configuration/save-train-parameters),
`Metadata.Input` holds the input as JSON, so a unique value in the input finds the run among any
others in the same container. The `trax-hub` template's train test does both:

```csharp from=Trax.Samples/templates/content/Trax.Samples.Hub/tests/Trax.Samples.Hub.Tests/IntegrationTests/HelloWorldTrainTests.cs
    [SetUp]
    public void BuildTheContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
                .AddMediator(typeof(HelloWorldTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    [Test]
    public async Task RunAsync_WithAName_CompletesAndIsRecorded()
    {
        // A name no other run uses, to find this run's record among any others.
        var name = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        await bus.RunAsync<Unit>(new HelloWorldInput { Name = name });

        using var scope = _provider.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var run = data
            .Metadatas.Where(m => m.Name == typeof(IHelloWorldTrain).FullName)
            .AsEnumerable()
            .SingleOrDefault(m => m.Input?.Contains(name) == true);

        Assert.That(run, Is.Not.Null, "every run of a train is recorded, with its input");
        Assert.That(run!.TrainState, Is.EqualTo(TrainState.Completed));
    }
```

A failed run's row has `TrainState.Failed`, and its `FailureJunction`, `FailureException` and
`FailureReason` say which junction threw and why. The columns are in
[Metadata](/docs/effect/metadata).

## Pick the data provider

The in-memory provider runs no SQL. When the code under test does, run the test on a provider
that does:

| The code under test uses | Provider |
|---|---|
| Model logic only, no queries | `UseInMemory()` |
| LINQ queries EF translates (`Where`, `OrderBy`, `Select`) | `UseInMemory()` or `UseSqlite(connectionString)` |
| Raw SQL (`ExecuteSqlRawAsync`, `Database.GetDbConnection()`) | `UseSqlite(connectionString)` or `UsePostgres(connectionString)` |
| Postgres features (`FOR UPDATE SKIP LOCKED`, advisory locks, `pg_class`) | `UsePostgres(connectionString)` |

A Postgres test needs a database of its own; [Test a host end to end](/docs/cross-cutting/e2e-testing#give-the-suite-its-own-database)
sets one up.

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [UseSqlite](/docs/sdk-reference/configuration/use-sqlite) | [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [RunAsync](/docs/sdk-reference/mediator-api/i-train-bus) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters)
