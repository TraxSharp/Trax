using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Trax.Samples.EnergyHub.E2E.Fixtures;

namespace Trax.Samples.EnergyHub.E2E.Factories;

/// <summary>
/// A worker: claims the jobs the hub writes to <c>background_job</c> and runs them, publishing
/// lifecycle events to RabbitMQ. Same database and broker as <see cref="EnergyHubFactory"/>.
/// </summary>
public class EnergyWorkerFactory : WebApplicationFactory<Trax.Samples.EnergyHub.Worker.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting(
            "ConnectionStrings:TraxDatabase",
            TestPostgres.WithPort(EnergyHubFactory.ConnectionString)
        );
        builder.UseSetting("ConnectionStrings:RabbitMQ", TestRabbitMq.ConnectionString);
    }
}
