using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Trax.Api.Exceptions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// How queueTrain and requeueExecution report a train authorization failure to a GraphQL caller: as
/// <c>TRAX_AUTHORIZATION</c>, like the rest of the API, rather than as a failed result, and without
/// the reason the caller was refused, which the exception carries for the server's logs.
///
/// <para>The operations service is substituted, so this pins the error's shape and not the
/// decision to refuse. That the operations service authorizes against the real mediator is
/// <c>OperationsServiceAuthorizationTests</c> in Trax.Scheduler.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0017-a-callers-enqueue-goes-through-the-mediator.md")]
public class QueueTrainAuthorizationTests
{
    private const string QueueTrainMutation = """
        mutation {
          operations {
            workQueue {
              queueTrain(input: { trainName: "Trax.X.IGuardedTrain", inputJson: "{}" }) {
                success
              }
            }
          }
        }
        """;

    [Test]
    public async Task An_authorization_failure_reaches_the_caller_as_TRAX_AUTHORIZATION_without_its_reason()
    {
        var operations = Substitute.For<IOperationsService>();
        operations
            .QueueTrainAsync(Arg.Any<QueueTrainInput>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(
                new TrainAuthorizationException(
                    "Trax.X.IGuardedTrain",
                    "missing role GuardedTrainOperators"
                )
            );

        using var host = await StartHostAsync(operations);

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: null,
            QueueTrainMutation
        );

        AdminOperationsAuthorizationTests
            .HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue("it is refused like any other authorization failure, not as a failed result");
        doc.RootElement.GetRawText()
            .Should()
            .NotContain(
                "GuardedTrainOperators",
                "which requirement was missing is the server's to know, not the caller's"
            );

        await host.StopAsync();
    }

    [Test]
    public async Task A_requeue_refused_by_train_authorization_reaches_the_caller_as_TRAX_AUTHORIZATION()
    {
        // requeueExecution reads the train and input off the saved run, then enqueues through
        // the same operations service as queueTrain, so the refusal has to surface the same way.
        var dataContextFactory = new InMemoryContextProviderFactory(
            new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot()
        );
        long id;
        await using (var db = await dataContextFactory.CreateDbContextAsync(default))
        {
            var meta = Metadata.Create(
                new CreateMetadata
                {
                    Name = "Trax.X.IGuardedTrain",
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                }
            );
            meta.Input = "{\"v\": 1}";
            await db.Track(meta);
            await db.SaveChanges(default);
            id = meta.Id;
        }

        var operations = Substitute.For<IOperationsService>();
        operations
            .QueueTrainAsync(Arg.Any<QueueTrainInput>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(
                new TrainAuthorizationException(
                    "Trax.X.IGuardedTrain",
                    "missing role GuardedTrainOperators"
                )
            );

        using var host = await StartHostAsync(operations, dataContextFactory);

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: null,
            $$"""
            mutation {
              operations {
                requeueExecution(id: {{id}}) {
                  success
                }
              }
            }
            """
        );

        AdminOperationsAuthorizationTests
            .HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue(
                "a requeue is an enqueue, refused like queueTrain rather than as a failed result"
            );
        doc.RootElement.GetRawText()
            .Should()
            .NotContain(
                "GuardedTrainOperators",
                "which requirement was missing is the server's to know, not the caller's"
            );
        await operations
            .Received(1)
            .QueueTrainAsync(
                Arg.Is<QueueTrainInput>(i => i!.TrainName == "Trax.X.IGuardedTrain"),
                Arg.Any<CancellationToken>()
            );

        await host.StopAsync();
    }

    private static async Task<IHost> StartHostAsync(
        IOperationsService operations,
        IDataContextProviderFactory? dataContextFactory = null
    )
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddAuthentication();
                        services.AddAuthorization();

                        services.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());

                        services.AddTraxGraphQL(graphql =>
                            graphql
                                .ExposeOperationQueries()
                                .ExposeOperationMutations()
                                .AllowAnonymousOperations()
                        );

                        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
                        services.AddScoped(_ => operations);
                        services.AddScoped(_ =>
                            Substitute.For<Trax.Mediator.Services.TrainExecution.ITrainExecutionService>()
                        );
                        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
                        if (dataContextFactory is not null)
                            services.AddSingleton(dataContextFactory);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }
}
