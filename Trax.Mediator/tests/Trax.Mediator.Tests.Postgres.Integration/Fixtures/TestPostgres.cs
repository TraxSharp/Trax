using Npgsql;

namespace Trax.Mediator.Tests.Postgres.Integration.Fixtures;

/// <summary>
/// The port of the test Postgres. <c>TRAX_TEST_PG_PORT</c> overrides it for a machine where another
/// database already holds 5432; unset or empty, it stays 5432, which is what CI's service container uses.
/// </summary>
internal static class TestPostgres
{
    public static int Port { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("TRAX_TEST_PG_PORT"), out var port)
            ? port
            : 5432;

    public static string WithPort(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Port = Port }.ConnectionString;
}
