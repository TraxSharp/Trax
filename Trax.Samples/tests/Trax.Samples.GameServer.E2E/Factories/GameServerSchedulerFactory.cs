using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Scheduler.Configuration;

namespace Trax.Samples.GameServer.E2E.Factories;

public class GameServerSchedulerFactory : WebApplicationFactory<Scheduler.Program>
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=trax_e2e_tests;Username=trax;Password=trax123;Maximum Pool Size=10";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:TraxDatabase", ConnectionString);

        builder.ConfigureTestServices(services =>
        {
            services.AddHostedService<ConfigureSchedulerForTestsService>();
        });
    }

    /// <summary>
    /// Configures the scheduler for test execution: fast polling, no automatic manifest
    /// scheduling (to prevent contention), high job limit.
    /// ManifestManager stays enabled initially for startup seeding, then gets disabled
    /// by tests after manifests are confirmed. The JobDispatcher stays enabled to
    /// dispatch manually-enqueued work queue entries.
    /// </summary>
    private sealed class ConfigureSchedulerForTestsService(SchedulerConfiguration config)
        : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            config.ManifestManagerPollingInterval = TimeSpan.FromSeconds(1);
            config.JobDispatcherPollingInterval = TimeSpan.FromSeconds(1);
            config.DefaultRetryDelay = TimeSpan.FromSeconds(2);
            config.DefaultJobTimeout = TimeSpan.FromSeconds(30);
            config.MaxActiveJobs = 100;

            // Fast metadata cleanup polling for tests (PeriodicTimer reads this at startup).
            if (config.MetadataCleanup is not null)
                config.MetadataCleanup.CleanupInterval = TimeSpan.FromSeconds(2);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
