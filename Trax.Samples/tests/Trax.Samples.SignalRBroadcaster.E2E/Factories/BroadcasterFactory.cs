using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Trax.Samples.SignalRBroadcaster.E2E.Factories;

/// <summary>The sample host in the environment a test names (Development unless told otherwise).</summary>
public class BroadcasterFactory(string environment = "Development") : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment(environment);
}
