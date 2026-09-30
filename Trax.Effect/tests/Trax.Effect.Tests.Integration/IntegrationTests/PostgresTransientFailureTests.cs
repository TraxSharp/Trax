using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// The Postgres dialect tells a failure worth retrying from one that fails the same way every
/// time, by the database error inside whatever wraps it rather than by the driver's namespace.
/// </summary>
public class PostgresTransientFailureTests : TestSetup
{
    private ISqlDialect Dialect => Scope.ServiceProvider.GetRequiredService<ISqlDialect>();

    [TestCase(PostgresErrorCodes.SerializationFailure)]
    [TestCase(PostgresErrorCodes.DeadlockDetected)]
    [TestCase(PostgresErrorCodes.TooManyConnections)]
    [TestCase(PostgresErrorCodes.CannotConnectNow)]
    public void A_server_error_postgres_lists_as_transient_is_transient(string sqlState)
    {
        Dialect.IsTransient(ServerError(sqlState)).Should().BeTrue();
    }

    [TestCase(PostgresErrorCodes.UniqueViolation)]
    [TestCase(PostgresErrorCodes.CheckViolation)]
    [TestCase(PostgresErrorCodes.UndefinedTable)]
    [TestCase(PostgresErrorCodes.SyntaxError)]
    public void A_server_error_that_fails_the_same_way_every_time_is_not_transient(string sqlState)
    {
        Dialect
            .IsTransient(ServerError(sqlState))
            .Should()
            .BeFalse("retrying cannot change the answer, however the driver names the type");
    }

    [Test]
    public void A_transient_error_inside_what_ef_wraps_it_in_is_transient()
    {
        var saved = new DbUpdateException(
            "save failed",
            ServerError(PostgresErrorCodes.SerializationFailure)
        );
        var retried = new InvalidOperationException("retry limit", saved);

        Dialect.IsTransient(saved).Should().BeTrue();
        Dialect.IsTransient(retried).Should().BeTrue();
        Dialect.IsTransient(new AggregateException(new TimeoutException())).Should().BeTrue();
    }

    [Test]
    public void A_failure_with_no_database_error_in_it_is_not_transient()
    {
        Dialect.IsTransient(new InvalidOperationException("bad state")).Should().BeFalse();
    }

    [Test]
    public async Task A_refused_connection_is_transient()
    {
        var unreachable = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = 1,
            Username = "trax",
            Password = "trax123",
            Timeout = 2,
        };
        await using var connection = new NpgsqlConnection(unreachable.ConnectionString);

        var failure = await Capture(() => connection.OpenAsync());

        Dialect.IsTransient(failure).Should().BeTrue();
    }

    [Test]
    public async Task A_query_against_a_missing_table_is_not_transient()
    {
        using var context = (IDataContext)DataContextFactory.Create();

        var failure = await Capture(() =>
            ((DbContext)context).Database.ExecuteSqlRawAsync("SELECT 1 FROM trax.no_such_table")
        );

        failure.Should().BeOfType<PostgresException>();
        Dialect.IsTransient(failure).Should().BeFalse();
    }

    private static PostgresException ServerError(string sqlState) =>
        new("server error", "ERROR", "ERROR", sqlState);

    private static async Task<Exception> Capture(Func<Task> act)
    {
        try
        {
            await act();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new AssertionException("expected the call to fail");
    }
}
