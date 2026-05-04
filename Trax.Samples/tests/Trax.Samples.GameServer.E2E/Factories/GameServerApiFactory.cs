using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Trax.Samples.GameServer.E2E.Factories;

public class GameServerApiFactory : WebApplicationFactory<Api.Program>
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=trax_e2e_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=4;Minimum Pool Size=0;Connection Idle Lifetime=1;Connection Pruning Interval=1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:TraxDatabase", ConnectionString);
    }
}
