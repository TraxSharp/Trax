using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Data.Sqlite.Services.SqliteContext;
using Trax.Effect.Data.Sqlite.Services.SqliteContextFactory;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

[TestFixture]
public class SqliteBuilderFlagTests
{
    private string _dbPath = null!;
    private ServiceProvider _provider = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_builder_test_{Guid.NewGuid():N}.db");

        _provider = new ServiceCollection()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseSqlite($"Data Source={_dbPath}"))
            )
            .BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        await _provider.DisposeAsync();

        if (File.Exists(_dbPath))
            File.Delete(_dbPath);

        var walPath = _dbPath + "-wal";
        if (File.Exists(walPath))
            File.Delete(walPath);

        var shmPath = _dbPath + "-shm";
        if (File.Exists(shmPath))
            File.Delete(shmPath);
    }

    #region Builder Flags

    [Test]
    public void UseSqlite_SetsHasDataProvider_True()
    {
        var context = _provider.GetService<IDataContext>();
        context
            .Should()
            .NotBeNull("HasDataProvider should be true, so IDataContext must be registered");
    }

    [Test]
    public void UseSqlite_SetsHasDatabaseProvider_True()
    {
        var factory = _provider.GetService<IDbContextFactory<SqliteContext>>();
        factory
            .Should()
            .NotBeNull(
                "HasDatabaseProvider should be true, so DbContextFactory must be registered"
            );
    }

    #endregion

    #region Service Registrations

    [Test]
    public void UseSqlite_RegistersISqlDialect_AsSqliteSqlDialect()
    {
        var dialect = _provider.GetRequiredService<ISqlDialect>();
        dialect.GetType().Name.Should().Be("SqliteSqlDialect");
    }

    [Test]
    public void UseSqlite_RegistersIDataContext_AsSqliteContext()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        context.Should().BeOfType<SqliteContext>();
    }

    [Test]
    public void UseSqlite_RegistersIDataContextProviderFactory_AsSqliteContextProviderFactory()
    {
        using var scope = _provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        factory.Should().BeOfType<SqliteContextProviderFactory>();
    }

    #endregion

    #region Data Context Logging

    [Test]
    public void UseSqlite_EnablesDataContextLogging()
    {
        // DataContextLogging is enabled when UseSqlite sets DataContextLoggingEffectEnabled = true.
        // The Log DbSet should be available on the context, confirming data context logging support.
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();

        context
            .Logs.Should()
            .NotBeNull("data context logging should make the Logs DbSet available");
    }

    #endregion

    #region Builder Promotion

    [Test]
    public void UseSqlite_PromotesToTraxEffectBuilderWithData()
    {
        // UseSqlite returns TraxEffectBuilderWithData, which enables data-dependent
        // extensions like AddJunctionProgress. Verify by checking that junction-progress
        // dependent services can be chained. We already verified IDataContext and
        // IDataContextProviderFactory are registered, which only happens when the
        // builder is promoted to TraxEffectBuilderWithData.
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetService<IDataContext>();
        var factory = scope.ServiceProvider.GetService<IDataContextProviderFactory>();

        context.Should().NotBeNull();
        factory.Should().NotBeNull();
    }

    #endregion
}
