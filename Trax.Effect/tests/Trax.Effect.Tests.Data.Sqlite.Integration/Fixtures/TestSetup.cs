using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Data.Sqlite.Utils;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.Fixtures;

/// <summary>
/// Base test fixture for SQLite integration tests.
/// Uses a temp file database so the schema persists across connections.
/// </summary>
public abstract class TestSetup
{
    private ServiceProvider ServiceProvider { get; set; } = null!;
    private string _dbPath = null!;

    public IServiceScope Scope { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task RunBeforeAnyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";

        var services = new ServiceCollection();

        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects
                    .SetEffectLogLevel(Microsoft.Extensions.Logging.LogLevel.Debug)
                    .UseSqlite(connectionString)
            )
        );

        ServiceProvider = ConfigureServices(services);
    }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests()
    {
        await ServiceProvider.DisposeAsync();

        // Clean up the temp database file
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);

        var walPath = _dbPath + "-wal";
        if (File.Exists(walPath))
            File.Delete(walPath);

        var shmPath = _dbPath + "-shm";
        if (File.Exists(shmPath))
            File.Delete(shmPath);
    }

    [SetUp]
    public virtual async Task TestSetUp()
    {
        Scope = ServiceProvider.CreateScope();
    }

    [TearDown]
    public async Task TestTearDown()
    {
        Scope.Dispose();
    }

    public abstract ServiceProvider ConfigureServices(IServiceCollection services);
}
