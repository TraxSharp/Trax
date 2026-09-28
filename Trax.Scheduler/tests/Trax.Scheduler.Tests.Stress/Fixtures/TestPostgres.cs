using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Trax.Scheduler.Tests.Stress.Fixtures;

/// <summary>
/// Where the test Postgres lives. The connection string comes from <c>appsettings.json</c>, with its port
/// taken from <c>TRAX_TEST_PG_PORT</c> for a machine where another database already holds 5432. Unset or
/// empty, the port stays 5432, which is what CI's service container uses.
/// </summary>
internal static class TestPostgres
{
    public static int Port { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("TRAX_TEST_PG_PORT"), out var port)
            ? port
            : 5432;

    public static string ConnectionString { get; } =
        new NpgsqlConnectionStringBuilder(
            new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build()
                .GetRequiredSection("Configuration")["DatabaseConnectionString"]!
        )
        {
            Port = Port,
        }.ConnectionString;
}
