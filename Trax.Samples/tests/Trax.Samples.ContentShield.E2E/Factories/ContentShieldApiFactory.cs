using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Trax.Samples.ContentShield.E2E.Fixtures;

namespace Trax.Samples.ContentShield.E2E.Factories;

/// <summary>
/// The API, started in Development as <c>dotnet run</c> starts it, pointed at a
/// <see cref="TestRunner"/> instead of localhost:5205.
/// </summary>
public class ContentShieldApiFactory(string runnerBaseUrl)
    : WebApplicationFactory<Trax.Samples.ContentShield.Api.Program>
{
    internal const string ConnectionString =
        "Host=localhost;Port=5432;Database=contentshield_e2e_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=4;Minimum Pool Size=0;Connection Idle Lifetime=1;Connection Pruning Interval=1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting(
            "ConnectionStrings:TraxDatabase",
            TestPostgres.WithPort(ConnectionString)
        );
        builder.UseSetting("ConnectionStrings:RabbitMQ", TestRabbitMq.ConnectionString);
        builder.UseSetting("Runner:BaseUrl", runnerBaseUrl);
    }
}
