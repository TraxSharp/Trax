using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// A dispatch mutation for a train that can be queued enqueues it under its canonical name (the
/// service interface's FullName) with the caller's input and priority, and returns the receipt.
/// </summary>
[TestFixture]
public class DispatchQueueModeTests
{
    private ITrainExecutionService _execution = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public void SetUp() => _execution = Substitute.For<ITrainExecutionService>();

    [TearDown]
    public async Task TearDown() => await _services.DisposeAsync();

    [Test]
    public async Task ModeQueue_EnqueuesUnderTheFullName_WithTheInputAndPriority()
    {
        _execution
            .QueueAsync(
                typeof(IReportTrain).FullName!,
                Arg.Any<string?>(),
                7,
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new QueueTrainResult(42, "ext-42"));
        var executor = await BuildAsync(GraphQLOperation.Run | GraphQLOperation.Queue);

        var data = await RunAsync(
            executor,
            """mutation { dispatch { report(input: { name: "q3" }, mode: QUEUE, priority: 7) { workQueueId externalId } } }"""
        );

        data.GetProperty("workQueueId").GetInt64().Should().Be(42);
        data.GetProperty("externalId").GetString().Should().Be("ext-42");
        await _execution
            .Received(1)
            .QueueAsync(
                typeof(IReportTrain).FullName!,
                Arg.Is<string?>(json => json != null && json.Contains("q3")),
                7,
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
        await _execution
            .DidNotReceiveWithAnyArgs()
            .RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task QueueOnlyTrain_EnqueuesWithPriorityZeroWhenNoneIsGiven()
    {
        _execution
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new QueueTrainResult(5, "ext-5"));
        var executor = await BuildAsync(GraphQLOperation.Queue);

        var data = await RunAsync(
            executor,
            """mutation { dispatch { report(input: { name: "q3" }) { workQueueId } } }"""
        );

        data.GetProperty("workQueueId").GetInt64().Should().Be(5);
        await _execution
            .Received(1)
            .QueueAsync(
                typeof(IReportTrain).FullName!,
                Arg.Any<string?>(),
                0,
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static async Task<JsonElement> RunAsync(IRequestExecutor executor, string mutation)
    {
        var body = (await executor.ExecuteAsync(mutation)).ExpectOperationResult().ToJson();
        using var json = JsonDocument.Parse(body);
        json.RootElement.TryGetProperty("errors", out _).Should().BeFalse(body);
        return json
            .RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("report")
            .Clone();
    }

    private async Task<IRequestExecutor> BuildAsync(GraphQLOperation operations)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([Registration(operations)]);
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
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

    private static TrainRegistration Registration(GraphQLOperation operations) =>
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
            GraphQLOperations = operations,
            IsRemote = false,
        };

    private interface IReportTrain;

    private sealed class ReportTrain;

    public sealed record ReportInput
    {
        public string Name { get; init; } = "";
    }
}
