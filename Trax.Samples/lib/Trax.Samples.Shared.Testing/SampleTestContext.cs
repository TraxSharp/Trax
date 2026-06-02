using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Trax.Samples.Shared.Testing;

/// <summary>
/// Builds a domain data context against the test provider matrix (the EF in-memory provider and
/// SQLite), so a context derived from the shared base can be round-trip tested without a real
/// PostgreSQL instance.
/// </summary>
public static class SampleTestContext
{
    /// <summary>
    /// Creates a context backed by the EF in-memory provider with a unique database name, so each
    /// call is isolated. Schema-less, so it exercises the base's schema-skipping path.
    /// </summary>
    public static TContext InMemory<TContext>()
        where TContext : DbContext =>
        Construct<TContext>(
            new DbContextOptionsBuilder<TContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    /// <summary>
    /// Creates a context backed by a private in-memory SQLite database. The returned holder owns
    /// the open connection (the in-memory database lives only while the connection is open) and the
    /// context; dispose it when done. The schema is created via <c>EnsureCreated</c>.
    /// </summary>
    public static SqliteContextHolder<TContext> Sqlite<TContext>()
        where TContext : DbContext
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var context = Construct<TContext>(
            new DbContextOptionsBuilder<TContext>().UseSqlite(connection).Options
        );
        context.Database.EnsureCreated();

        return new SqliteContextHolder<TContext>(connection, context);
    }

    private static TContext Construct<TContext>(DbContextOptions<TContext> options)
        where TContext : DbContext =>
        (TContext)Activator.CreateInstance(typeof(TContext), options)!;
}

/// <summary>
/// Owns an open in-memory SQLite connection and the context bound to it. Disposing closes both,
/// dropping the in-memory database.
/// </summary>
public sealed class SqliteContextHolder<TContext>(SqliteConnection connection, TContext context)
    : IDisposable
    where TContext : DbContext
{
    public TContext Context { get; } = context;

    public void Dispose()
    {
        Context.Dispose();
        connection.Dispose();
    }
}
