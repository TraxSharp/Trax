using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.DTOs;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// End-to-end tests for the Trax GraphQL operations queries and mutations.
/// Builds a real HotChocolate request executor and executes actual GraphQL operations.
/// </summary>
[TestFixture]
public class GraphQLOperationsTests
{
    private ITraxScheduler _scheduler = null!;
    private ITraxHealthService _healthService = null!;
    private ITrainDiscoveryService _discoveryService = null!;
    private ServiceProvider _serviceProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _scheduler = Substitute.For<ITraxScheduler>();
        _healthService = Substitute.For<ITraxHealthService>();
        _discoveryService = Substitute.For<ITrainDiscoveryService>();

        // TrainTypeModule dynamically generates DispatchMutations and DiscoverQueries fields
        // from discovered trains. HotChocolate requires at least one field per object type,
        // so we provide separate fake query and mutation registrations (TrainTypeModule uses
        // if/else — a registration goes to queries OR mutations, not both).
        _discoveryService
            .DiscoverTrains()
            .Returns([
                new TrainRegistration
                {
                    ServiceType = typeof(IFakeQueryTrain),
                    ImplementationType = typeof(FakeQueryTrain),
                    InputType = typeof(FakeGraphQLInput),
                    OutputType = typeof(Unit),
                    Lifetime = ServiceLifetime.Scoped,
                    ServiceTypeName = nameof(IFakeQueryTrain),
                    ImplementationTypeName = nameof(FakeQueryTrain),
                    InputTypeName = nameof(FakeGraphQLInput),
                    OutputTypeName = nameof(Unit),
                    RequiredPolicies = [],
                    RequiredRoles = [],
                    IsQuery = true,
                    IsMutation = false,
                    IsRemote = false,
                    IsBroadcastEnabled = false,
                    GraphQLOperations = GraphQLOperation.Run,
                },
                new TrainRegistration
                {
                    ServiceType = typeof(IFakeMutationTrain),
                    ImplementationType = typeof(FakeMutationTrain),
                    InputType = typeof(FakeMutationInput),
                    OutputType = typeof(Unit),
                    Lifetime = ServiceLifetime.Scoped,
                    ServiceTypeName = nameof(IFakeMutationTrain),
                    ImplementationTypeName = nameof(FakeMutationTrain),
                    InputTypeName = nameof(FakeMutationInput),
                    OutputTypeName = nameof(Unit),
                    RequiredPolicies = [],
                    RequiredRoles = [],
                    IsQuery = false,
                    IsMutation = true,
                    IsRemote = false,
                    IsBroadcastEnabled = false,
                    GraphQLOperations = GraphQLOperation.Run | GraphQLOperation.Queue,
                },
            ]);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();
    }

    #region Query Tests

    [Test]
    public async Task GetHealth_ReturnsHealthStatus()
    {
        // Arrange
        _healthService
            .GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new HealthStatus("Healthy", "All systems operational", 3, 1, 0, 0));

        var executor = await BuildExecutor();

        // Act
        var result = await executor.ExecuteAsync(
            """
            {
                operations {
                    health {
                        status
                        description
                        queueDepth
                        inProgress
                        failedLastHour
                        deadLetters
                    }
                }
            }
            """
        );

        // Assert
        var operationResult = result as IOperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().BeNullOrEmpty();
        var json = operationResult.ToJson();
        json.Should().Contain("Healthy");
        json.Should().Contain("All systems operational");
    }

    [Test]
    public async Task GetTrains_ReturnsEmptyList()
    {
        // Arrange
        var executor = await BuildExecutor();

        // Act
        var result = await executor.ExecuteAsync(
            """
            {
                operations {
                    trains {
                        serviceTypeName
                    }
                }
            }
            """
        );

        // Assert
        var operationResult = result as IOperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().BeNullOrEmpty();
    }

    #endregion

    #region Mutation Tests

    [Test]
    public async Task TriggerManifest_CallsScheduler()
    {
        // Arrange
        var executor = await BuildExecutor();

        // Act
        var result = await executor.ExecuteAsync(
            """
            mutation {
                operations {
                    triggerManifest(externalId: "test-job") {
                        success
                        message
                    }
                }
            }
            """
        );

        // Assert
        var operationResult = result as IOperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().BeNullOrEmpty();
        var json = operationResult.ToJson();
        json.Should().Contain("true");
        json.Should().Contain("Manifest triggered");
        await _scheduler.Received(1).TriggerAsync("test-job", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DisableManifest_CallsScheduler()
    {
        // Arrange
        var executor = await BuildExecutor();

        // Act
        var result = await executor.ExecuteAsync(
            """
            mutation {
                operations {
                    disableManifest(externalId: "test-job") {
                        success
                        message
                    }
                }
            }
            """
        );

        // Assert
        var operationResult = result as IOperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().BeNullOrEmpty();
        await _scheduler.Received(1).DisableAsync("test-job", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EnableManifest_CallsScheduler()
    {
        // Arrange
        var executor = await BuildExecutor();

        // Act
        var result = await executor.ExecuteAsync(
            """
            mutation {
                operations {
                    enableManifest(externalId: "test-job") {
                        success
                        message
                    }
                }
            }
            """
        );

        // Assert
        var operationResult = result as IOperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().BeNullOrEmpty();
        await _scheduler.Received(1).EnableAsync("test-job", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CancelManifest_ReturnsCount()
    {
        // Arrange
        _scheduler.CancelAsync("test-job", Arg.Any<CancellationToken>()).Returns(2);

        var executor = await BuildExecutor();

        // Act
        var result = await executor.ExecuteAsync(
            """
            mutation {
                operations {
                    cancelManifest(externalId: "test-job") {
                        success
                        count
                        message
                    }
                }
            }
            """
        );

        // Assert
        var operationResult = result as IOperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().BeNullOrEmpty();
        var json = operationResult.ToJson();
        json.Should().Contain("Cancellation requested");
    }

    [Test]
    public async Task TriggerGroup_ReturnsCount()
    {
        // Arrange
        _scheduler.TriggerGroupAsync(42L, Arg.Any<CancellationToken>()).Returns(3);

        var executor = await BuildExecutor();

        // Act
        var result = await executor.ExecuteAsync(
            """
            mutation {
                operations {
                    triggerGroup(groupId: 42) {
                        success
                        count
                        message
                    }
                }
            }
            """
        );

        // Assert
        var operationResult = result as IOperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().BeNullOrEmpty();
    }

    #endregion

    #region Helper Methods

    private async Task<IRequestExecutor> BuildExecutor()
    {
        var services = new ServiceCollection();

        // Register TraxMarker (required by AddTraxGraphQL)
        services.AddSingleton<Trax.Effect.Configuration.TraxBuilder.TraxMarker>();

        // Register discovery and effect registry before AddTraxGraphQL (needed during schema build)
        services.AddSingleton(_discoveryService);
        services.AddSingleton(Substitute.For<IEffectRegistry>());

        // Register GraphQL schema (this calls AddTraxApi which registers concrete services)
        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(services);

        // Register mocks AFTER AddTraxGraphQL so they override the concrete registrations
        services.AddScoped(_ => _healthService);
        services.AddScoped(_ => _scheduler);

        _serviceProvider = services.BuildServiceProvider();

        return await _serviceProvider
            .GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync("trax");
    }

    #endregion

    #region Test Types

    private interface IFakeQueryTrain;

    private class FakeQueryTrain;

    private interface IFakeMutationTrain;

    private class FakeMutationTrain;

    public record FakeGraphQLInput
    {
        public string Value { get; init; } = "";
    }

    public record FakeMutationInput
    {
        public string Data { get; init; } = "";
    }

    #endregion
}
