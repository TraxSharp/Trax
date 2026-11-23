using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Trax.Samples.Shared.Testing;

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
