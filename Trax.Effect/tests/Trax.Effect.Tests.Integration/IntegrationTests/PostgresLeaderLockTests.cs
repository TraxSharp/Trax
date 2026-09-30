using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// <see cref="ISqlDialect.TryAcquireLeaderLock"/> on Postgres takes the advisory lock its name hashes
/// to, read back from <c>pg_locks</c> by a second session. Two different names are two locks.
/// </summary>
[TestFixture]
public class PostgresLeaderLockTests : TestSetup
{
    private ISqlDialect Dialect => Scope.ServiceProvider.GetRequiredService<ISqlDialect>();

    [Test]
    public async Task The_leader_lock_is_keyed_by_the_hash_of_its_name()
    {
        var name = $"leader-{Guid.NewGuid():N}";

        using var context = (DbContext)
            await DataContextFactory.CreateDbContextAsync(CancellationToken.None);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var acquired = await context
            .Database.SqlQuery<bool>(Dialect.TryAcquireLeaderLock(name))
            .SingleAsync();
        acquired.Should().BeTrue();

        (await HeldAdvisoryLocksHashing(name))
            .Should()
            .Be(1, "the lock taken must be the one hashtext(name) names, not a fixed key");
    }

    [Test]
    public async Task Two_names_are_two_locks()
    {
        var first = $"leader-{Guid.NewGuid():N}";
        var second = $"leader-{Guid.NewGuid():N}";

        using var holder = (DbContext)
            await DataContextFactory.CreateDbContextAsync(CancellationToken.None);
        await using var holderTransaction = await holder.Database.BeginTransactionAsync();
        (await holder.Database.SqlQuery<bool>(Dialect.TryAcquireLeaderLock(first)).SingleAsync())
            .Should()
            .BeTrue();

        using var other = (DbContext)
            await DataContextFactory.CreateDbContextAsync(CancellationToken.None);
        await using var otherTransaction = await other.Database.BeginTransactionAsync();
        (await other.Database.SqlQuery<bool>(Dialect.TryAcquireLeaderLock(second)).SingleAsync())
            .Should()
            .BeTrue("a lock under another name is not the one already held");
    }

    /// <summary>
    /// Counts the granted advisory locks on the single 64-bit key <c>hashtext(name)</c>. Postgres
    /// shows that key split in two: the high 32 bits in <c>classid</c>, the low 32 in <c>objid</c>,
    /// with <c>objsubid = 1</c> marking the single-key form.
    /// </summary>
    private static async Task<long> HeldAdvisoryLocksHashing(string name)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        await using var connection = new NpgsqlConnection(
            TestPostgres.WithPort(
                configuration.GetRequiredSection("Configuration")["DatabaseConnectionString"]!
            )
        );
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*) FROM pg_locks
            WHERE locktype = 'advisory' AND granted AND objsubid = 1
              AND objid::bigint = (hashtext(@name)::bigint & 4294967295)
              AND classid::bigint = ((hashtext(@name)::bigint >> 32) & 4294967295)
            """;
        command.Parameters.AddWithValue("name", name);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
