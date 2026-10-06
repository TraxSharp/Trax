using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.Services.HealthCheck;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.TraxScheduler;
using IOperationsService = Trax.Scheduler.Services.Operations.IOperationsService;
using OperationsService = Trax.Scheduler.Services.Operations.OperationsService;

namespace Trax.Api.Tests;

/// <summary>
/// A manifest mutation naming an external id no manifest has is a refusal the caller can act on:
/// <c>success: false</c> with the reason, no GraphQL error, and nothing asked of the scheduler.
/// Enforces <c>docs/adr/0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md</c>.
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md")]
public class ManifestMutationRefusalTests
{
    private const string KnownId = "known-job";

    private ITraxScheduler _scheduler = null!;
    private ServiceProvider _provider = null!;
    private IRequestExecutor _executor = null!;

    [SetUp]
    public async Task SetUp()
    {
        _scheduler = Substitute.For<ITraxScheduler>();
        var factory = new InMemoryContextProviderFactory(new InMemoryDatabaseRoot());

        await using (var db = await factory.CreateDbContextAsync(default))
        {
            var manifest = Manifest.Create(new CreateManifest { Name = typeof(IRefusalTrain) });
            manifest.ExternalId = KnownId;
            await db.Track(manifest);
            await db.SaveChanges(default);
        }

        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery
            .DiscoverTrains()
            .Returns([
                new TrainRegistration
                {
                    ServiceType = typeof(IRefusalTrain),
                    ImplementationType = typeof(RefusalTrain),
                    InputType = typeof(RefusalInput),
                    OutputType = typeof(Unit),
                    Lifetime = ServiceLifetime.Scoped,
                    ServiceTypeName = nameof(IRefusalTrain),
                    ImplementationTypeName = nameof(RefusalTrain),
                    HasAllowAnonymousAttribute = true,
                    InputTypeName = nameof(RefusalInput),
                    OutputTypeName = nameof(Unit),
                    RequiredPolicies = [],
                    RequiredRoles = [],
                    IsQuery = false,
                    IsMutation = true,
                    IsRemote = false,
                    IsBroadcastEnabled = false,
                    GraphQLOperations = GraphQLOperation.Run,
                },
            ]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Trax.Effect.Configuration.TraxBuilder.TraxMarker>();
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton<IDataContextProviderFactory>(factory);
        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            graphql =>
                graphql
                    .ExposeOperationQueries()
                    .ExposeOperationMutations()
                    .AllowAnonymousOperations()
        );
        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
        services.AddScoped(_ => _scheduler);
        // The triggers go through the shared operations service, over the same store.
        services.AddScoped<IOperationsService>(sp => new OperationsService(
            discovery,
            factory,
            new SchedulerConfiguration(),
            Substitute.For<ITrainExecutionService>(),
            sp
        ));

        _provider = services.BuildServiceProvider();
        _executor = await _provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    [TearDown]
    public async Task TearDown() => await _provider.DisposeAsync();

    private static IEnumerable<TestCaseData> ManifestMutations() =>
        TriggerMutations().Concat(SchedulerMutations());

    private static IEnumerable<TestCaseData> TriggerMutations()
    {
        yield return new TestCaseData("triggerManifest(externalId: \"{0}\")").SetArgDisplayNames(
            "triggerManifest"
        );
        yield return new TestCaseData(
            "triggerManifestDelayed(externalId: \"{0}\", delay: \"PT5M\")"
        ).SetArgDisplayNames("triggerManifestDelayed");
    }

    private static IEnumerable<TestCaseData> SchedulerMutations()
    {
        yield return new TestCaseData("disableManifest(externalId: \"{0}\")").SetArgDisplayNames(
            "disableManifest"
        );
        yield return new TestCaseData("enableManifest(externalId: \"{0}\")").SetArgDisplayNames(
            "enableManifest"
        );
        yield return new TestCaseData("cancelManifest(externalId: \"{0}\")").SetArgDisplayNames(
            "cancelManifest"
        );
    }

    private async Task<(bool Success, string Message, string Json)> RunAsync(
        string field,
        string externalId
    )
    {
        var result = await _executor.ExecuteAsync(
            "mutation { operations { "
                + string.Format(field, externalId)
                + " { success message count } } }"
        );
        var operation = (OperationResult)result;
        var json = operation.ToJson();
        operation
            .Errors.Should()
            .BeNullOrEmpty(
                "an unknown manifest is a refusal, not a server failure "
                    + "(docs/adr/0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md): "
                    + json
            );

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var payload = doc
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .EnumerateObject()
            .Single()
            .Value;
        return (
            payload.GetProperty("success").GetBoolean(),
            payload.GetProperty("message").GetString()!,
            json
        );
    }

    [TestCaseSource(nameof(ManifestMutations))]
    public async Task UnknownExternalId_IsRefusedWithTheReason(string field)
    {
        var (success, message, _) = await RunAsync(field, "no-such-job");

        success.Should().BeFalse();
        message.Should().Be("Manifest 'no-such-job' not found.");
        _scheduler.ReceivedCalls().Should().BeEmpty("a refusal asks nothing of the scheduler");
    }

    [TestCaseSource(nameof(SchedulerMutations))]
    public async Task KnownExternalId_IsPassedToTheScheduler(string field)
    {
        var (success, _, _) = await RunAsync(field, KnownId);

        success.Should().BeTrue();
        _scheduler.ReceivedCalls().Should().ContainSingle();
    }

    [TestCaseSource(nameof(TriggerMutations))]
    public async Task KnownExternalId_OnAnInMemoryStore_TriggerIsRefusedAndNothingQueued(
        string field
    )
    {
        var (success, message, _) = await RunAsync(field, KnownId);

        success.Should().BeFalse("nothing dispatches a queued run on an in-memory store");
        message.Should().Be(OperationsService.NoDispatcherMessage);
        await using var db = await _provider
            .GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(default);
        db.WorkQueues.Should().BeEmpty();
    }

    private interface IRefusalTrain;

    private class RefusalTrain;

    public record RefusalInput
    {
        public string Value { get; init; } = "";
    }
}
