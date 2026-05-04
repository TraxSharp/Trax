using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Samples.JobHunt.Providers.Llm;
using Trax.Scheduler.Configuration;

namespace Trax.Samples.JobHunt.E2E.Factories;

public class JobHuntHubFactory : WebApplicationFactory<Hub.Program>
{
    // Pin pool size and prune idle connections aggressively so a long suite of
    // fresh test hosts in CI can't exhaust Postgres' max_connections.
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=jobhunt_e2e_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=4;Minimum Pool Size=0;Connection Idle Lifetime=1;Connection Pruning Interval=1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:TraxDatabase", ConnectionString);

        builder.ConfigureTestServices(services =>
        {
            // Replace the real Ollama provider with a deterministic stub.
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ILlmProvider));
            if (descriptor is not null)
                services.Remove(descriptor);
            services.AddSingleton<ILlmProvider, StubLlmProvider>();

            // Configure scheduler for fast test execution.
            services.AddHostedService<ConfigureSchedulerForTestsService>();
        });
    }

    private sealed class ConfigureSchedulerForTestsService(SchedulerConfiguration config)
        : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            config.ManifestManagerPollingInterval = TimeSpan.FromSeconds(1);
            config.JobDispatcherPollingInterval = TimeSpan.FromSeconds(1);
            config.MaxActiveJobs = 100;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
