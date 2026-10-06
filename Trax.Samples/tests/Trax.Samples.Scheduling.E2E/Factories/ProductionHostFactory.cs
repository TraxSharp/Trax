using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Trax.Samples.Scheduling.E2E.Factories;

/// <summary>
/// The same host started in Production, to prove what Production leaves closed. Its scheduler's
/// background services are removed, because the Development host of
/// <see cref="SharedSchedulingSetup"/> runs against the same database at the same time
/// and these tests read only the host's HTTP surface.
/// </summary>
public sealed class ProductionHostFactory : SchedulingHostFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Production");

        builder.ConfigureTestServices(services =>
        {
            foreach (
                var hosted in services
                    .Where(d =>
                        d.ServiceType == typeof(IHostedService)
                        && (
                            d.ImplementationType?.Namespace?.StartsWith("Trax.Scheduler") == true
                            || d.ImplementationFactory?.Method.ReturnType.Namespace?.StartsWith(
                                "Trax.Scheduler"
                            ) == true
                        )
                    )
                    .ToList()
            )
                services.Remove(hosted);
        });
    }
}
