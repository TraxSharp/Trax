using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.Exceptions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// A train mutation's <c>mode: QUEUE</c> on a host whose store is in memory (EF Core's InMemory
/// provider) is refused with <c>TRAX_QUEUE_UNAVAILABLE</c> and nothing is enqueued, since no
/// job dispatcher could ever run the entry. <c>mode: RUN</c> is unaffected, and a host with a
/// database still queues.
///
/// <para>Enforces <c>docs/adr/0038-a-queued-mutation-is-refused-where-nothing-dispatches-it.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0038-a-queued-mutation-is-refused-where-nothing-dispatches-it.md")]
[TestFixture]
public class QueueWithoutDispatcherTests
{
    private const string Because =
        "nothing dispatches a queued run on a host whose store is in memory "
        + "(docs/adr/0038-a-queued-mutation-is-refused-where-nothing-dispatches-it.md)";

    private ITrainExecutionService _execution = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _execution = Substitute.For<ITrainExecutionService>();
        _execution
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new QueueTrainResult(42, "ext-42"));
    }

    [TearDown]
    public async Task TearDown() => await _services.DisposeAsync();

    [Test]
    public async Task ModeQueue_OnAnInMemoryStore_IsRefusedAndNothingIsQueued()
    {
        var executor = await BuildAsync(withDatabase: false);

        var body = (
            await executor.ExecuteAsync(
                """mutation { dispatch { report(input: { name: "q3" }, mode: QUEUE) { workQueueId } } }"""
            )
        )
            .ExpectOperationResult()
            .ToJson();

        using var json = JsonDocument.Parse(body);
        var error = json.RootElement.GetProperty("errors")[0];
        error
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("TRAX_QUEUE_UNAVAILABLE", Because);
        error.GetProperty("message").GetString().Should().Contain("mode: RUN");
        await _execution
            .DidNotReceiveWithAnyArgs()
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task ModeQueue_OnAnInMemoryStore_ByACallerTheTrainRefuses_IsNotAuthorized()
    {
        _execution
            .PrepareAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<PreparedTrain>>(_ =>
                throw new TrainAuthorizationException(typeof(IReportTrain).FullName!, "role")
            );
        var executor = await BuildAsync(withDatabase: false);

        var body = (
            await executor.ExecuteAsync(
                """mutation { dispatch { report(input: { name: "q3" }, mode: QUEUE) { workQueueId } } }"""
            )
        )
            .ExpectOperationResult()
            .ToJson();

        using var json = JsonDocument.Parse(body);
        var error = json.RootElement.GetProperty("errors")[0];
        error
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(
                "TRAX_AUTHORIZATION",
                "the train's authorization is applied before the host says how its store is configured"
            );
        error.GetProperty("message").GetString().Should().Be("Not authorized.");
        body.Should().NotContain("in memory");
        await _execution
            .DidNotReceiveWithAnyArgs()
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task ModeQueue_WithADatabase_Queues()
    {
        var executor = await BuildAsync(withDatabase: true);

        var body = (
            await executor.ExecuteAsync(
                """mutation { dispatch { report(input: { name: "q3" }, mode: QUEUE) { workQueueId } } }"""
            )
        )
            .ExpectOperationResult()
            .ToJson();

        using var json = JsonDocument.Parse(body);
        json.RootElement.TryGetProperty("errors", out _).Should().BeFalse(body);
        json.RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("report")
            .GetProperty("workQueueId")
            .GetInt64()
            .Should()
            .Be(42);
    }

    private async Task<IRequestExecutor> BuildAsync(bool withDatabase)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([Registration()]);
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        // A relational store is stood in for by a factory whose contexts are not EF Core's
        // InMemory provider; the in-memory one is the real thing.
        services.AddSingleton<IDataContextProviderFactory>(
            withDatabase
                ? Substitute.For<IDataContextProviderFactory>()
                : new InMemoryContextProviderFactory(new InMemoryDatabaseRoot())
        );
        services.AddTraxGraphQL(graphql =>
            graphql.ExposeOperationQueries().AllowAnonymousOperations()
        );
        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
        services.AddScoped(_ => Substitute.For<IOperationsService>());
        services.AddScoped(_ => _execution);
        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
        _services = services.BuildServiceProvider();
        return await _services
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    private static TrainRegistration Registration() =>
        new()
        {
            ServiceType = typeof(IReportTrain),
            ImplementationType = typeof(ReportTrain),
            InputType = typeof(ReportInput),
            OutputType = typeof(Trax.Core.Functional.Unit),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = typeof(IReportTrain).FullName!,
            ImplementationTypeName = nameof(ReportTrain),
            InputTypeName = nameof(ReportInput),
            OutputTypeName = nameof(Trax.Core.Functional.Unit),
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = true,
            IsBroadcastEnabled = false,
            HasAllowAnonymousAttribute = true,
            GraphQLName = "report",
            GraphQLOperations = GraphQLOperation.Run | GraphQLOperation.Queue,
            IsRemote = false,
        };

    private interface IReportTrain;

    private sealed class ReportTrain;

    public sealed record ReportInput
    {
        public string Name { get; init; } = "";
    }
}
