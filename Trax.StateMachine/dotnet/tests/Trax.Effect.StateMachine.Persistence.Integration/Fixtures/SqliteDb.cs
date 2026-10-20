using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

/// <summary>
/// A SQLite file built by the shipped migrations, with stores over the data context and dialect <c>UseSqlite</c>
/// registers, deleted on dispose.
/// </summary>
public sealed class SqliteDb : IDisposable
{
    private readonly string _file = Path.Combine(
        Path.GetTempPath(),
        $"sm_sqlite_{Guid.NewGuid():N}.db"
    );

    private ServiceProvider _host = null!;

    public static Task<SqliteDb> Create()
    {
        var db = new SqliteDb();
        var services = new ServiceCollection();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseSqlite($"Data Source={db._file}"))
        );
        db._host = services.BuildServiceProvider();
        return Task.FromResult(db);
    }

    public IDataContext NewContext() =>
        (IDataContext)_host.GetRequiredService<IDataContextProviderFactory>().Create();

    private ISqlDialect Dialect => _host.GetRequiredService<ISqlDialect>();

    public EfEffectClaimStore NewClaims() => new(NewContext(), Dialect);

    public EfSnapshotStore NewStore() => new(NewContext(), Dialect);

    public void Dispose()
    {
        _host.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_file);
        }
        catch (IOException)
        {
            // Best effort: a lingering handle can hold the temp file; the OS reaps it later.
        }
    }
}
