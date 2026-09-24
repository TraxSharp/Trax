using System.Text.Json;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// What a caller reading the work queue through GraphQL can see of staging and subjects:
/// <c>confirmedAt</c>, null while a deferred enqueue is still staged, and <c>subjectKey</c>, the
/// subject whose runs are serialised (central <c>docs/0019</c>). Asked through the schema, so a
/// field dropped from the type fails here even though the DTO still carries it.
/// </summary>
[TestFixture]
[Property(
    "adr",
    "Trax.Docs/adr/0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md"
)]
public class WorkQueueSummarySchemaTests
{
    private ServiceProvider? _serviceProvider;

    [TearDown]
    public async Task TearDown()
    {
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();
    }

    [Test]
    public async Task A_work_queue_entry_exposes_whether_it_is_confirmed_and_its_subject()
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([]);

        var services = new ServiceCollection();
        services.AddSingleton<Trax.Effect.Configuration.TraxBuilder.TraxMarker>();
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            graphql => graphql.ExposeOperationQueries().AllowAnonymousOperations()
        );
        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
        services.AddScoped(_ => Substitute.For<ITraxScheduler>());

        _serviceProvider = services.BuildServiceProvider();

        var executor = await _serviceProvider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        var result = await executor.ExecuteAsync(
            """{ __type(name: "WorkQueueSummary") { fields { name type { kind name } } } }"""
        );

        var operationResult = result as OperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().BeNullOrEmpty();

        using var document = JsonDocument.Parse(operationResult.ToJson());
        var fields = document
            .RootElement.GetProperty("data")
            .GetProperty("__type")
            .GetProperty("fields")
            .EnumerateArray()
            .ToDictionary(
                f => f.GetProperty("name").GetString()!,
                f => f.GetProperty("type").Clone()
            );

        fields
            .Should()
            .ContainKeys(
                ["confirmedAt", "subjectKey"],
                "a staged entry is told apart from a confirmed one by confirmedAt, and runs "
                    + "serialised behind a sibling are told apart by subjectKey"
            );
        fields["confirmedAt"]
            .GetProperty("kind")
            .GetString()
            .Should()
            .NotBe("NON_NULL", "a staged entry has no confirmation time yet");
        fields["subjectKey"]
            .GetProperty("kind")
            .GetString()
            .Should()
            .NotBe("NON_NULL", "most entries carry no subject");
    }
}
