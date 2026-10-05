using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Samples.EnergyHub.E2E.Fixtures;
using Trax.Scheduler.Configuration;

namespace Trax.Samples.EnergyHub.E2E.Factories;

/// <summary>
/// The hub: GraphQL, scheduler and dashboard, started in Development as <c>dotnet run</c> starts it.
/// It runs no jobs; <see cref="EnergyWorkerFactory"/> does.
/// </summary>
public class EnergyHubFactory : WebApplicationFactory<Trax.Samples.EnergyHub.Hub.Program>
{
    internal const string ConnectionString =
        "Host=localhost;Port=5432;Database=energyhub_e2e_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=4;Minimum Pool Size=0;Connection Idle Lifetime=1;Connection Pruning Interval=1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting(
            "ConnectionStrings:TraxDatabase",
            TestPostgres.WithPort(ConnectionString)
        );
        builder.UseSetting("ConnectionStrings:RabbitMQ", TestRabbitMq.ConnectionString);

        builder.ConfigureServices(services =>
        {
            services.AddHostedService<ConfigureSchedulerForTestsService>();
        });
    }

    private class ConfigureSchedulerForTestsService(SchedulerConfiguration config) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            config.ManifestManagerPollingInterval = TimeSpan.FromSeconds(1);
            config.JobDispatcherPollingInterval = TimeSpan.FromSeconds(1);
            config.DefaultRetryDelay = TimeSpan.FromSeconds(2);
            config.DefaultJobTimeout = TimeSpan.FromSeconds(30);
            config.MaxActiveJobs = 100;

            if (config.MetadataCleanup is not null)
                config.MetadataCleanup.CleanupInterval = TimeSpan.FromSeconds(2);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
