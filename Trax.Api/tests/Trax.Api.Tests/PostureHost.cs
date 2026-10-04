using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// A TestServer host for the posture suites: an open endpoint, an API key per role combination,
/// and whatever query models and schema additions the test configures. Requests go through the
/// HTTP pipeline, so authentication, <c>@authorize</c> and the error filter all run.
/// </summary>
internal static class PostureHost
{
    public const string AdminKey = "posture-admin";
    public const string SupportKey = "posture-support";
    public const string AdminSupportKey = "posture-admin-support";
    public const string ReaderKey = "posture-reader";

    /// <summary>A registered policy that requires the <c>Admin</c> role.</summary>
    public const string AdminPolicy = "PostureAdmin";

    public static Task<IHost> StartAsync(
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder> configure,
        Action<IServiceCollection>? services = null,
        IReadOnlyList<TrainRegistration>? trains = null
    ) =>
        new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(s =>
                    {
                        s.AddLogging();
                        s.AddRouting();
                        s.AddTraxApiKeyAuth(keys =>
                            keys.Add(AdminKey, id: "admin", "Admin")
                                .Add(SupportKey, id: "support", "Support")
                                .Add(AdminSupportKey, id: "both", "Admin", "Support")
                                .Add(ReaderKey, id: "reader")
                        );
                        s.AddAuthorization(o =>
                            o.AddPolicy(AdminPolicy, p => p.RequireRole("Admin"))
                        );
                        s.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns(trains ?? []);
                        s.AddSingleton(discovery);
                        s.AddSingleton(Substitute.For<IEffectRegistry>());
                        s.AddSingleton(Substitute.For<ITraxScheduler>());
                        s.AddSingleton(Substitute.For<IOperationsService>());
                        s.AddSingleton(Substitute.For<ITrainExecutionService>());
                        s.AddSingleton(Substitute.For<ITraxHealthService>());
                        s.AddSingleton(Substitute.For<IJobSubmitter>());
                        services?.Invoke(s);
                        s.AddTraxGraphQL(configure);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .StartAsync();

    /// <summary>Registers an InMemory context factory and seeds it.</summary>
    public static void AddInMemoryContext<TContext>(
        this IServiceCollection services,
        Action<TContext>? seed = null
    )
        where TContext : DbContext
    {
        var name = typeof(TContext).Name + "-" + Guid.NewGuid();
        services.AddDbContextFactory<TContext>(o => o.UseInMemoryDatabase(name));
        if (seed is null)
            return;

        var options = new DbContextOptionsBuilder<TContext>().UseInMemoryDatabase(name).Options;
        using var context = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
        seed(context);
        context.SaveChanges();
    }

    public static async Task<string> PostAsync(this IHost host, string query, string? apiKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { query }),
                Encoding.UTF8,
                "application/json"
            ),
        };
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);

        var response = await host.GetTestClient().SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }
}
