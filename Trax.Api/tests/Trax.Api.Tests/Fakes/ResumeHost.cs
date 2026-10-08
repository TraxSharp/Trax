using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Tests.AuthE2E;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Fakes;

/// <summary>
/// A host over Postgres that runs <see cref="ResumableTrainBase"/>'s trains for real, saving
/// their inputs, junction events and checkpoints, and serves the operations surface behind a gate
/// for the <c>Operator</c> role, with the real <see cref="OperationsService"/>. Nothing dispatches
/// the work queue, so a queued resume stays queued for a test to read. The dashboard's calls are
/// made as the dashboard makes them: the operations service, inside the <c>"dashboard"</c>
/// trusted scope.
/// </summary>
internal sealed class ResumeHost : IAsyncDisposable
{
    /// <summary>Past the operations gate, but not in the guarded train's <c>Resumer</c> role.</summary>
    public const string OperatorKey = "resume-operator-key";

    /// <summary>Past the gate and in the <c>Resumer</c> role.</summary>
    public const string ResumerKey = "resume-resumer-key";

    private readonly IHost _host;

    private ResumeHost(IHost host) => _host = host;

    public IServiceProvider Services => _host.Services;

    public static async Task<ResumeHost> StartAsync(string database)
    {
        AuthE2EHost.EnsureDatabaseExists(database);
        var connectionString = AuthE2EHost.ConnectionString(database);

        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddTraxApiKeyAuth(keys =>
                            keys.Add(OperatorKey, id: "operator", "Operator")
                                .Add(ResumerKey, id: "resumer", "Operator", "Resumer")
                        );
                        // The scanned trains include one gated by AdminPolicy, which must be registered.
                        services.AddAuthorization(o =>
                            o.AddPolicy("AdminPolicy", p => p.RequireRole("Admin"))
                        );

                        services.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                    effects
                                        .UsePostgres(connectionString)
                                        .AddJunctionEvents()
                                        .SaveTrainParameters()
                                )
                                .AddMediator(typeof(ResumeHost).Assembly)
                        );
                        services.AddTraxApi();
                        services.AddTraxGraphQL(graphql =>
                            graphql
                                .ExposeOperationQueries()
                                .ExposeOperationMutations()
                                .GateOperations(roles: "Operator")
                        );

                        // The operations service as an API-only host registers it. The scheduler
                        // and the submitter are what the mutations' startup check asks for;
                        // nothing here dispatches or runs a queued entry.
                        services.AddSingleton(new SchedulerConfiguration());
                        services.AddScoped<IOperationsService, OperationsService>();
                        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
                        services.AddScoped(_ => Substitute.For<IJobSubmitter>());
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
        return new ResumeHost(host);
    }

    /// <summary>
    /// Runs <typeparamref name="TTrain"/> to its end, failing in <paramref name="failIn"/> when
    /// given, and returns the run's id.
    /// </summary>
    public async Task<long> RunAsync<TTrain>(string? failIn, string topic = "graphs")
        where TTrain : IServiceTrain<ResumableInput, string>
    {
        ResumeProbe.FailIn = failIn;
        try
        {
            using var scope = Services.CreateScope();
            var train = scope.ServiceProvider.GetRequiredService<TTrain>();
            try
            {
                await train.Run(new ResumableInput { Topic = topic });
            }
            catch (TimeoutException) when (failIn is not null)
            {
                // The run fails where it was told to.
            }

            await FlushJunctionEventsAsync();
            return ((ServiceTrain<ResumableInput, string>)(object)train).Metadata!.Id;
        }
        finally
        {
            ResumeProbe.FailIn = null;
        }
    }

    /// <summary>
    /// Waits until the host's junction event writer has written every step queued so far: it
    /// writes in the background, so a read straight after a run can miss its last steps.
    /// </summary>
    private async Task FlushJunctionEventsAsync()
    {
        // The writer and its flush are internal to Trax.Effect.Data, which this assembly cannot see.
        var writer = Services.GetRequiredService(
            typeof(Effect.Data.Extensions.ServiceExtensions).Assembly.GetType(
                "Trax.Effect.Data.JunctionEvents.JunctionRunWriter",
                throwOnError: true
            )!
        );
        await (
            (Task)
                writer
                    .GetType()
                    .GetMethod(
                        "FlushAsync",
                        System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.NonPublic
                    )!
                    .Invoke(writer, [CancellationToken.None])!
        ).WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Runs the queued resume <paramref name="entryId"/> here, as the dispatcher would hand it to
    /// a worker: a run carrying the entry's input and the run and node it resumes.
    /// </summary>
    public async Task<long> RunResumeAsync<TTrain>(long entryId)
        where TTrain : IServiceTrain<ResumableInput, string>
    {
        var entry = await With(d => d.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == entryId));
        using var scope = Services.CreateScope();
        var train =
            (ServiceTrain<ResumableInput, string>)
                (object)scope.ServiceProvider.GetRequiredService<TTrain>();
        var metadata = Metadata.Create(
            new Effect.Models.Metadata.DTOs.CreateMetadata
            {
                Name = entry.TrainName,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ResumeFrom = entry.ResumeFrom,
                ResumeAt = entry.ResumeAt,
            }
        );
        await train.Run(JsonSerializer.Deserialize<ResumableInput>(entry.Input!)!, metadata);
        await FlushJunctionEventsAsync();
        return train.Metadata!.Id;
    }

    /// <summary>The GraphQL response to <paramref name="query"/>, sent with <paramref name="apiKey"/>.</summary>
    public async Task<JsonDocument> GraphQLAsync(string query, string? apiKey = OperatorKey)
    {
        var client = _host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { query }),
        };
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);

        var response = await client.SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary><c>operations.resumeExecution</c> over HTTP, as a GraphQL caller sends it.</summary>
    public Task<JsonDocument> ResumeOverGraphQLAsync(
        long id,
        string? from,
        string? apiKey = OperatorKey
    ) =>
        GraphQLAsync(
            $$"""
            mutation {
              operations {
                resumeExecution(id: {{id}}{{(
                from is null ? "" : $", from: {JsonSerializer.Serialize(from)}"
            )}}) {
                  success
                  message
                  id
                  count
                }
              }
            }
            """,
            apiKey
        );

    /// <summary>The dashboard's resume: the operations service, inside its trusted scope.</summary>
    public async Task<OperationResult> ResumeAsTheDashboardAsync(long id, string? from)
    {
        using var scope = Services.CreateScope();
        var trusted = scope.ServiceProvider.GetRequiredService<ITrustedExecutionScope>();
        using (trusted.BeginTrusted("dashboard"))
            return await scope
                .ServiceProvider.GetRequiredService<IOperationsService>()
                .ResumeExecutionAsync(id, from, CancellationToken.None);
    }

    /// <summary>The queued entries that resume <paramref name="runId"/>.</summary>
    public Task<List<WorkQueue>> QueuedResumesOf(long runId) =>
        With(d =>
            d.WorkQueues.AsNoTracking()
                .Where(q => q.ResumeFrom == runId && q.Status == WorkQueueStatus.Queued)
                .ToListAsync()
        );

    public async Task<T> With<T>(Func<IDataContext, Task<T>> read)
    {
        await using var context = await Services
            .GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        return await read(context);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}
