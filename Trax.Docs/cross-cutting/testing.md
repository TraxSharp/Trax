---
layout: default
title: Testing
description: How to unit test junctions and trains, run integration tests on the InMemory provider, test cancellation and Blazor components, and choose a test data provider.
parent: Cross-Cutting
nav_order: 3
---

# Testing

The examples use NUnit. The assertions are NUnit's constraint model (`Assert.That`), which needs
no extra package; Trax's own repositories assert with AwesomeAssertions instead, an Apache-2.0 fork of
FluentAssertions 7 that is free for commercial use (see
[Test Conventions](/docs/reference/test-conventions)), and either works. A test project needs
`Microsoft.NET.Test.Sdk`, `NUnit` and `NUnit3TestAdapter`, plus
`Microsoft.AspNetCore.Mvc.Testing` to start a host, and a `ProjectReference` to the application.
Every [project template](/docs/reference/templates) ships one set up this way under `tests/`.

## Unit Testing Junctions

Junctions are easy to test because they're just classes with a `Run` method. Create simple fake implementations of your dependencies:

```csharp
// A simple fake repository for testing
public class FakeUserRepository : IUserRepository
{
    private readonly List<User> _users = [];
    private int _nextId = 1;

    public Task<User?> GetByEmailAsync(string email)
        => Task.FromResult(_users.FirstOrDefault(u => u.Email == email));

    public Task<User> CreateAsync(User user)
    {
        user = user with { Id = _nextId++ };
        _users.Add(user);
        return Task.FromResult(user);
    }

    // Seed data for tests
    public void AddExisting(User user) => _users.Add(user);
}

[Test]
public async Task ValidateEmailJunction_ThrowsForDuplicateEmail()
{
    // Arrange
    var repo = new FakeUserRepository();
    repo.AddExisting(new User { Id = 1, Email = "taken@example.com" });

    var junction = new ValidateEmailJunction(repo);
    var request = new CreateUserRequest { Email = "taken@example.com" };

    // Act & Assert
    Assert.ThrowsAsync<ValidationException>(() => junction.Run(request));
}

[Test]
public async Task CreateUserJunction_ReturnsNewUser()
{
    // Arrange
    var repo = new FakeUserRepository();
    var junction = new CreateUserJunction(repo);
    var request = new CreateUserRequest
    {
        Email = "new@example.com",
        FirstName = "Test",
        LastName = "User"
    };

    // Act
    var result = await junction.Run(request);

    // Assert
    Assert.That(result.Id, Is.EqualTo(1), "the first user gets id 1");
    Assert.That(result.Email, Is.EqualTo("new@example.com"));
}
```

## Unit Testing Trains

Register your fakes in the service collection, then run the train through `ITrainBus`, the way the
application does:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;   // UseInMemory, package Trax.Effect.Data.InMemory
using Trax.Effect.Extensions;                 // AddTrax, AddEffects
using Trax.Mediator.Extensions;               // AddMediator
using Trax.Mediator.Services.TrainBus;        // ITrainBus

[Test]
public async Task CreateUserTrain_CreatesUser()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddSingleton<IUserRepository, FakeUserRepository>();
    services.AddSingleton<IEmailService, FakeEmailService>();
    services.AddLogging();
    services.AddTrax(trax => trax
        .AddEffects(effects => effects.UseInMemory())
        .AddMediator(typeof(CreateUserTrain).Assembly)
    );

    var provider = services.BuildServiceProvider();
    var bus = provider.GetRequiredService<ITrainBus>();

    // Act
    var result = await bus.RunAsync<User>(new CreateUserRequest
    {
        Email = "test@example.com",
        FirstName = "Test",
        LastName = "User"
    });

    // Assert
    Assert.That(result.Email, Is.EqualTo("test@example.com"));
}
```

`RunAsync` throws the exception that stopped the chain, so a failing junction fails the test with
its own exception.

## Integration Testing with InMemory Provider

For integration tests, use the InMemory data provider to avoid database dependencies. Every run
leaves a `Metadata` row, readable through the scoped `IDataContext`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;  // IDataContext
using Trax.Effect.Enums;                      // TrainState
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

[Test]
public async Task Train_PersistsMetadata()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddSingleton<IUserRepository, FakeUserRepository>();
    services.AddLogging();
    services.AddTrax(trax => trax
        .AddEffects(effects => effects
            .UseInMemory()
        )
        .AddMediator(typeof(CreateUserTrain).Assembly)
    );

    var provider = services.BuildServiceProvider();
    var bus = provider.GetRequiredService<ITrainBus>();

    // Act
    await bus.RunAsync<User>(new CreateUserRequest { Email = "test@example.com" });

    // Assert
    using var scope = provider.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
    var metadata = await context.Metadatas
        .Where(m => m.Name == typeof(ICreateUserTrain).FullName)
        .SingleAsync();
    Assert.That(metadata.TrainState, Is.EqualTo(TrainState.Completed));
}
```

`Metadata.Name` is the full name of the train's interface. The in-memory provider's store is shared
by every container in the test process, so a test that runs alongside others should pick out its
own run, by name and by something unique in its input (with
[`SaveTrainParameters()`](/docs/sdk-reference/configuration/save-train-parameters), `Metadata.Input`
holds the input as JSON).

## Testing the Host

`WebApplicationFactory<Program>` (package `Microsoft.AspNetCore.Mvc.Testing`) starts the whole
application in memory, `Program.cs` included, so a test fails when the host no longer starts or
when something meant for Development reaches Production. The top-level `Program` class is public in
.NET 10, so nothing needs adding to `Program.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

[Test]
public async Task Production_Dashboard_IsNotServed()
{
    await using var app = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(host => host.UseEnvironment("Production"));

    var response = await app.CreateClient().GetAsync("/trax");

    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
}

[Test]
public async Task Development_Dispatch_WithTheDemoKey_Runs()
{
    await using var app = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(host => host.UseEnvironment("Development"));
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("X-Api-Key", "demo-key-do-not-use-in-production");

    var response = await client.PostAsJsonAsync("/trax/graphql", new
    {
        query = "mutation { dispatch { helloWorld(input: { name: \"Test\" }) { metadataId } } }",
    });

    var body = await response.Content.ReadAsStringAsync();
    Assert.That(body, Does.Not.Contain("\"errors\""), body);
}
```

A GraphQL request answers HTTP 200 even when it is refused: the refusal is an entry in the
response's `errors` array with `extensions.code` `TRAX_AUTHORIZATION`. Assert on the body, not the
status code. The `trax-hub` template's `tests/<Name>.Tests/IntegrationTests/HostTests.cs` is a
complete version of these tests.

## Testing Cancellation

Verify that your junctions and trains handle cancellation correctly by passing a pre-cancelled or timed token:

```csharp
[Test]
public async Task Train_WithCancelledToken_DoesNotExecuteJunctions()
{
    // Arrange
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    var train = new MyTrain();

    // Act & Assert: train should throw, junction should not run
    var act = () => train.Run(input, cts.Token);
    await act.Should().ThrowAsync<Exception>();
}

[Test]
public async Task Junction_UsesToken_ForAsyncOperations()
{
    // Arrange
    using var cts = new CancellationTokenSource();
    var train = new TestTrain(new MyJunction());

    // Act
    await train.Run("input", cts.Token);

    // Assert: verify the junction received the token
    // (access via a test helper that captures this.CancellationToken)
}
```

*Full details: [Cancellation Tokens](/docs/cross-cutting/cancellation-tokens#testing-with-cancellation-tokens)*

## E2E Testing

For full application validation (scheduler dispatch, dependency chains, dormant dependent activation, dead-letter flows, and GraphQL authorization), use `WebApplicationFactory<T>`-based E2E tests against a real Postgres database.

*Full details: [E2E Testing](/docs/cross-cutting/e2e-testing)*

## Testing Blazor Components with bUnit

The dashboard is built on Blazor Server + Radzen. Component tests use [bUnit](https://bunit.dev/) to render and interact with components without a browser.

Set up the test context in `[SetUp]` and dispose it in `[TearDown]`:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

[TestFixture]
public class MyComponentTests
{
    // Disambiguate from NUnit.Framework.TestContext
    private Bunit.TestContext _ctx = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;  // for components that call IJSRuntime
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public void MyComponent_RendersExpectedMarkup()
    {
        var component = _ctx.RenderComponent<MyComponent>(p =>
            p.Add(x => x.Label, "hello")
        );

        component.Markup.Should().Contain("hello");
        component.Find("button").Click();
        component.Markup.Should().Contain("clicked");
    }
}
```

Key points:
- Always alias `Bunit.TestContext`. It collides with `NUnit.Framework.TestContext`.
- Components that inject `IJSRuntime` need `JSRuntimeMode.Loose` (or explicit handler setup).
- Pages that resolve scoped services often need a full Trax registration (`AddTrax`) plus a real database. Add the deeper services per test.

## Choosing a Data Provider for Tests

Trax ships three data providers: `UseInMemory()`, `UseSqlite()`, and `UsePostgres()`. They are not interchangeable for every test.

| Use case | Provider |
|---|---|
| Pure model logic, no SQL | InMemory |
| Standard EF queries (Where, OrderBy, Select) | InMemory or SQLite |
| Raw SQL via `ExecuteSqlRawAsync`, `Database.GetDbConnection()` | SQLite or Postgres |
| Postgres-specific features (`pg_class`, advisory locks, `FOR UPDATE SKIP LOCKED`, `make_interval`) | Postgres only |

The `CountEstimator` in `Trax.Api.GraphQL` is the canonical example. An unfiltered list of a large table reports the row estimate the provider's [ISqlDialect](/docs/sdk-reference/configuration/i-sql-dialect) returns from `EstimateRowCount()`, which on Postgres reads `pg_class.reltuples`. SQLite's dialect has no estimate and InMemory registers no dialect, so on either of them the same list counts exactly and a test never reaches the estimate. A test of the estimate seeds more rows than the estimate threshold, runs `ANALYZE` on the table (the estimate is what `ANALYZE` or autovacuum last measured), and runs on Postgres. Tests for `OperationsQueries` use `UsePostgres()` against the local `trax_database` container, and `TRUNCATE` the affected tables in `[SetUp]` for isolation:

```csharp
[SetUp]
public async Task SetUp()
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddTrax(trax => trax.AddEffects(e =>
        e.UsePostgres("Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123")
    ));
    _provider = services.BuildServiceProvider();
    _factory = _provider.GetRequiredService<IDataContextProviderFactory>();

    await using var db = await _factory.CreateDbContextAsync(default);
    await ((DbContext)db).Database.ExecuteSqlRawAsync(
        "TRUNCATE TABLE trax.dead_letter, trax.metadata, trax.manifest, trax.manifest_group "
        + "RESTART IDENTITY CASCADE"
    );
}
```

Start the database with `docker compose up -d` from `Trax.Samples/` before running tests that need it.

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect) | [AddMediator](/docs/sdk-reference/configuration/add-mediator) | [RunAsync](/docs/sdk-reference/mediator-api/train-bus)
