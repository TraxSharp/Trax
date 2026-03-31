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

    // Act & Assert — train should throw, junction should not run
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

    // Assert — verify the junction received the token
    // (access via a test helper that captures this.CancellationToken)
}
```

*Full details: [Cancellation Tokens](/docs/cross-cutting/cancellation-tokens#testing-with-cancellation-tokens)*

## E2E Testing

For full application validation — scheduler dispatch, dependency chains, dormant dependent activation, dead-letter flows, and GraphQL authorization — use `WebApplicationFactory<T>`-based E2E tests against a real Postgres database.

*Full details: [E2E Testing](/docs/cross-cutting/e2e-testing)*

## SDK Reference

> [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/add-in-memory-effect) | [AddMediator](/docs/sdk-reference/mediator-api/add-service-train-bus) | [RunAsync](/docs/sdk-reference/mediator-api/train-bus)
