using System.Text.Json;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.Services.HealthCheck;
using Trax.Core.Exceptions;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// How a failure's class reaches a GraphQL caller: the values it is spelled with, and the
/// <c>executions(failureClass:)</c> filter, both asked through the schema rather than by calling
/// the resolver, so a renamed enum value or an argument dropped from the field fails here.
///
/// <para>The resolver's own filtering and counting are <c>OperationsQueriesTests</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0020-a-failure-is-classified-where-it-happens-and-carried.md")]
public class FailureClassSchemaTests
{
    private ServiceProvider? _serviceProvider;

    [TearDown]
    public async Task TearDown()
    {
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();
    }

    [Test]
    public async Task The_FailureClass_values_a_caller_writes_are_pinned()
    {
        var executor = await BuildExecutorAsync(new InMemoryContextProviderFactory(new()));

        var data = await ExecuteAsync(
            executor,
            """{ __type(name: "FailureClass") { kind enumValues { name } } }"""
        );

        var type = data.GetProperty("__type");
        type.GetProperty("kind").GetString().Should().Be("ENUM");
        type.GetProperty("enumValues")
            .EnumerateArray()
            .Select(v => v.GetProperty("name").GetString())
            .Should()
            .BeEquivalentTo(
                ["UNCLASSIFIED", "TRANSIENT", "CONFLICT", "PERMANENT"],
                "a caller's query spells these values, so renaming one breaks every client that filters on it"
            );
    }

    [Test]
    public async Task Executions_filtered_on_CONFLICT_return_only_conflicts_and_count_them()
    {
        var factory = new InMemoryContextProviderFactory(new InMemoryDatabaseRoot());
        await SeedAsync(factory, FailureClass.Conflict, TrainState.Failed);
        await SeedAsync(factory, FailureClass.Transient, TrainState.Failed);
        await SeedAsync(factory, null, TrainState.Failed);
        await SeedAsync(factory, null, TrainState.Completed);

        var executor = await BuildExecutorAsync(factory);

        var data = await ExecuteAsync(
            executor,
            """
            {
              operations {
                failed: executions(trainState: FAILED) { totalCount }
                conflicts: executions(failureClass: CONFLICT) {
                  totalCount
                  items { failureClass trainState }
                }
              }
            }
            """
        );

        var operations = data.GetProperty("operations");
        operations
            .GetProperty("failed")
            .GetProperty("totalCount")
            .GetInt32()
            .Should()
            .Be(3, "three runs failed, and only one of them on a conflict");

        var conflicts = operations.GetProperty("conflicts");
        conflicts
            .GetProperty("totalCount")
            .GetInt32()
            .Should()
            .Be(1, "the count covers the filter, not the table");
        conflicts
            .GetProperty("items")
            .EnumerateArray()
            .Select(i => i.GetProperty("failureClass").GetString())
            .Should()
            .Equal(["CONFLICT"], "a transient or unclassified failure is not a conflict");
    }

    private static async Task SeedAsync(
        IDataContextProviderFactory factory,
        FailureClass? failureClass,
        TrainState state
    )
    {
        await using var db = await factory.CreateDbContextAsync(default);
        var meta = Metadata.Create(
            new CreateMetadata
            {
                Name = "Trax.X.Classified",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        meta.TrainState = state;
        if (failureClass is { } recorded)
            meta.AddException(
                new TrainException(
                    JsonSerializer.Serialize(
                        new TrainExceptionData
                        {
                            TrainName = "Classified",
                            TrainExternalId = meta.ExternalId,
                            Type = "SomeException",
                            Junction = "SomeJunction",
                            Message = "failed",
                            FailureClass = recorded,
                        }
                    )
                )
            );
        await db.Track(meta);
        await db.SaveChanges(default);
    }

    private async Task<IRequestExecutor> BuildExecutorAsync(IDataContextProviderFactory factory)
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([]);

        var services = new ServiceCollection();
        services.AddSingleton<Trax.Effect.Configuration.TraxBuilder.TraxMarker>();
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(factory);
        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            graphql => graphql.ExposeOperationQueries().AllowAnonymousOperations()
        );
        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
        services.AddScoped(_ => Substitute.For<ITraxScheduler>());

        _serviceProvider = services.BuildServiceProvider();

        return await _serviceProvider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    private static async Task<JsonElement> ExecuteAsync(IRequestExecutor executor, string query)
    {
        var result = await executor.ExecuteAsync(query);

        var operationResult = result as OperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().BeNullOrEmpty();

        using var document = JsonDocument.Parse(operationResult.ToJson());
        return document.RootElement.GetProperty("data").Clone();
    }
}
