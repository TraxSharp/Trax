using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Api.Tests.Auth;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.ConcurrencyLimiter;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// What a GraphQL caller sees when an operations mutation fails on the server's infrastructure
/// rather than being refused: a masked error carrying nothing about the server. The real
/// <see cref="OperationsService"/> runs here, over a mediator that fails the way a database
/// outage does, so the test covers the service's rethrow and the error filter's masking together.
/// Enforces <c>docs/adr/0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md</c>.
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md")]
public class OperationsFailureMaskingTests
{
    private const string TrainName = "Trax.Api.Tests.OperationsFailureMaskingTests+IMaskedTrain";

    private const string UnreachableHost = "10.0.0.5";

    [Test]
    public async Task QueueTrain_WhenTheDatabaseIsUnreachable_FailsMaskedWithNothingAboutTheServer()
    {
        var execution = Substitute.For<ITrainExecutionService>();
        execution
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new NpgsqlException($"Failed to connect to {UnreachableHost}:5432"));

        using var host = await StartHostAsync(execution, new RecordingSubmitter());

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            apiKey: null,
            $$"""
            mutation {
              operations {
                workQueue {
                  queueTrain(input: { trainName: "{{TrainName}}", inputJson: "{}" }) {
                    success
                    message
                  }
                }
              }
            }
            """
        );

        var raw = doc.RootElement.GetRawText();
        doc.RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeTrue(
                "an infrastructure failure is not a refusal, so it is not a failed result "
                    + "(docs/adr/0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md)"
            );
        errors[0].GetProperty("message").GetString().Should().Be("Unexpected Execution Error");
        raw.Should().NotContain(UnreachableHost).And.NotContain("Npgsql");

        await host.StopAsync();
    }

    internal static TrainRegistration Registration(IReadOnlyList<string>? requiredRoles = null) =>
        new()
        {
            ServiceType = typeof(IMaskedTrain),
            ImplementationType = typeof(MaskedTrain),
            InputType = typeof(MaskedInput),
            OutputType = typeof(Unit),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = nameof(IMaskedTrain),
            ImplementationTypeName = nameof(MaskedTrain),
            InputTypeName = nameof(MaskedInput),
            OutputTypeName = nameof(Unit),
            RequiredPolicies = [],
            RequiredRoles = requiredRoles ?? [],
            HasAuthorizeAttribute = requiredRoles is not null,
            IsQuery = false,
            IsMutation = false,
            IsRemote = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = GraphQLOperation.Run,
        };

    /// <param name="execution">
    /// The train execution service, or null for the mediator's own <see cref="TrainExecutionService"/>
    /// over this host's registry, so a run is authorized and its input read as in production.
    /// </param>
    internal static async Task<IHost> StartHostAsync(
        ITrainExecutionService? execution,
        IJobSubmitter submitter,
        TrainRegistration? registration = null,
        IDataContextProviderFactory? dataContextFactory = null
    )
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([registration ?? Registration()]);
        dataContextFactory ??= new InMemoryContextProviderFactory(
            new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot()
        );

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
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());
                        services.AddSingleton(dataContextFactory);

                        services.AddTraxGraphQL(graphql =>
                            graphql
                                .ExposeOperationQueries()
                                .ExposeOperationMutations()
                                .AllowAnonymousOperations()
                        );

                        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
                        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
                        if (execution is not null)
                            services.AddScoped(_ => execution);
                        else
                            services.AddScoped<ITrainExecutionService>(
                                sp => new TrainExecutionService(
                                    discovery,
                                    Substitute.For<IRunExecutor>(),
                                    Substitute.For<IConcurrencyLimiter>(),
                                    dataContextFactory,
                                    new MediatorConfiguration(),
                                    sp
                                )
                            );
                        services.AddScoped(_ => submitter);
                        // What AddMediator registers: the API's train authorization consults it.
                        services.AddScoped(_ => Substitute.For<ITrustedExecutionScope>());
                        services.AddScoped<IOperationsService>(sp => new OperationsService(
                            discovery,
                            dataContextFactory,
                            new SchedulerConfiguration(),
                            sp.GetRequiredService<ITrainExecutionService>(),
                            sp
                        ));
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

    /// <summary>
    /// A job submitter that records what it was handed, or fails the way an unreachable worker
    /// does. A class rather than a substitute, because the run calls one of the interface's
    /// default methods.
    /// </summary>
    internal sealed class RecordingSubmitter(Exception? failWith = null) : IJobSubmitter
    {
        public List<long> Submitted { get; } = [];

        public Task<string> EnqueueAsync(long metadataId) => EnqueueAsync(metadataId, new object());

        public Task<string> EnqueueAsync(long metadataId, object input)
        {
            if (failWith is not null)
                throw failWith;
            Submitted.Add(metadataId);
            return Task.FromResult($"job-{metadataId}");
        }
    }

    internal interface IMaskedTrain;

    internal sealed class MaskedTrain : IMaskedTrain;

    internal sealed record MaskedInput(string? Value = null);
}
