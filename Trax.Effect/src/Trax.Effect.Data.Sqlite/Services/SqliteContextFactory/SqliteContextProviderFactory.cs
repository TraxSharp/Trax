using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Services.SqliteContext;
using Trax.Effect.Services.EffectProvider;

namespace Trax.Effect.Data.Sqlite.Services.SqliteContextFactory;

/// <summary>
/// Creates <see cref="SqliteContext.SqliteContext"/> instances for the effect runner and for direct
/// data access. Infrastructure registered by <c>UseSqlite</c>; not intended to be constructed directly.
/// </summary>
/// <param name="dbContextFactory">The EF Core factory configured with the SQLite connection string.</param>
public class SqliteContextProviderFactory(
    IDbContextFactory<SqliteContext.SqliteContext> dbContextFactory
) : IDataContextProviderFactory
{
    private int _count;

    /// <summary>
    /// How many contexts this factory has created since it was constructed, through either method.
    /// It only counts up: disposing a context does not decrement it. Useful for spotting leaks in tests.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Creates a new <see cref="SqliteContext.SqliteContext"/> as an effect provider. The caller owns
    /// it and must dispose it.
    /// </summary>
    /// <returns>A new context, which is also an <see cref="IDataContext"/>.</returns>
    public IEffectProvider Create()
    {
        var context = dbContextFactory.CreateDbContext();
        Interlocked.Increment(ref _count);
        return context;
    }

    /// <inheritdoc/>
    /// <remarks>The caller owns the returned context and must dispose it.</remarks>
    public async Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken)
    {
        var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        Interlocked.Increment(ref _count);
        return context;
    }
}
