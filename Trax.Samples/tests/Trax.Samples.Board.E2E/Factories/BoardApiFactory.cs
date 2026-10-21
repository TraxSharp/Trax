using Microsoft.AspNetCore.Hosting;
using Trax.Samples.Board.E2E.Fixtures;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Board.E2E.Factories;

/// <summary>
/// Boots the whole Board host (GraphQL, scheduler, local workers) against a dedicated test database. Defaults to the
/// CI Postgres service (port 5432, database <c>board_e2e_tests</c>); <c>TRAX_TEST_PG_PORT</c> moves the port.
/// </summary>
public sealed class BoardApiFactory : SampleApiFactory<Api.Program>
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=board_e2e_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=20;Minimum Pool Size=0";

    protected override string ConnectionString => TestPostgres.WithPort(DefaultConnectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Development");
        // This suite drives the API with hand-written documents, as a client that wrote its own would; the agents'
        // generated client is checked against the host by CI's board-client job.
        builder.UseSetting("Board:RequirePersistedOperations", "false");
        builder.UseSetting("Board:MachineSweepInterval", "00:00:01");
    }
}
