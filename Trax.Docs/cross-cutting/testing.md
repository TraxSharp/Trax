---
layout: default
title: Testing
parent: Cross-Cutting
nav_order: 3
---

# Testing

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
    await Assert.ThrowsAsync<ValidationException>(() => junction.Run(request));
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
    Assert.Equal(1, result.Id);  // First user gets ID 1
    Assert.Equal("new@example.com", result.Email);
}
```

## Unit Testing Trains

Register your fakes in the service collection:

```csharp
[Test]
public async Task CreateUserTrain_CreatesUser()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddSingleton<IUserRepository, FakeUserRepository>();
    services.AddSingleton<IEmailService, FakeEmailService>();
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
    Assert.NotNull(result);
    Assert.Equal("test@example.com", result.Email);
}
```

## Integration Testing with InMemory Provider

For integration tests, use the InMemory data provider to avoid database dependencies:

```csharp
[Test]
public async Task Train_PersistsMetadata()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddSingleton<IUserRepository, FakeUserRepository>();
    services.AddTrax(trax => trax
        .AddEffects(effects => effects
            .UseInMemory()
        )
        .AddMediator(typeof(CreateUserTrain).Assembly)
    );

    var provider = services.BuildServiceProvider();
    var bus = provider.GetRequiredService<ITrainBus>();
    var context = provider.GetRequiredService<IDataContext>();

    // Act
    await bus.RunAsync<User>(new CreateUserRequest { Email = "test@example.com" });

    // Assert
    var metadata = await context.Metadatas.FirstOrDefaultAsync();
    Assert.NotNull(metadata);
    Assert.Equal(TrainState.Completed, metadata.TrainState);
}
```

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
- Polling components (`PollingComponentBase`) dispose themselves; pause polling via `PausePolling = true` if you want to assert on a single render.

## Choosing a Data Provider for Tests

Trax ships three data providers: `UseInMemory()`, `UseSqlite()`, and `UsePostgres()`. They are not interchangeable for every test.

| Use case | Provider |
|---|---|
| Pure model logic, no SQL | InMemory |
| Standard EF queries (Where, OrderBy, Select) | InMemory or SQLite |
| Raw SQL via `ExecuteSqlRawAsync`, `Database.GetDbConnection()` | SQLite or Postgres |
| Postgres-specific features (`pg_class`, advisory locks, `FOR UPDATE SKIP LOCKED`, `make_interval`) | Postgres only |

The `CountEstimator` in `Trax.Api.GraphQL` is the canonical example: it queries `pg_class.reltuples` for fast row-count estimates and only works on Postgres. Tests for `OperationsQueries` use `UsePostgres()` against the local `trax_database` container, and `TRUNCATE` the affected tables in `[SetUp]` for isolation:

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

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect) | [AddMediator](/docs/sdk-reference/mediator-api/add-service-train-bus) | [RunAsync](/docs/sdk-reference/mediator-api/train-bus)
