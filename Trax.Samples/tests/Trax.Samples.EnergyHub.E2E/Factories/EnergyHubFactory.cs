using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Scheduler.Configuration;

namespace Trax.Samples.EnergyHub.E2E.Factories;

public class EnergyHubFactory : WebApplicationFactory<Trax.Samples.EnergyHub.Hub.Program>
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=energyhub_e2e_tests;Username=trax;Password=trax123;Maximum Pool Size=10";

    private const string RabbitMqConnectionString = "amqp://guest:guest@localhost:5672";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:TraxDatabase", ConnectionString);
        builder.UseSetting("ConnectionStrings:RabbitMQ", RabbitMqConnectionString);

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
