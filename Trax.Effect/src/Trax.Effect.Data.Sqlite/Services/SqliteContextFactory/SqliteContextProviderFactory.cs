using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Services.SqliteContext;
using Trax.Effect.Services.EffectProvider;

namespace Trax.Effect.Data.Sqlite.Services.SqliteContextFactory;

/// <summary>
/// Factory for creating SQLite database contexts.
/// </summary>
public class SqliteContextProviderFactory(
    IDbContextFactory<SqliteContext.SqliteContext> dbContextFactory
) : IDataContextProviderFactory
{
    private int _count;

    public int Count => _count;

    public IEffectProvider Create()
    {
        var context = dbContextFactory.CreateDbContext();
        Interlocked.Increment(ref _count);
        return context;
    }

    public async Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken)
    {
        var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        Interlocked.Increment(ref _count);
        return context;
    }
}
