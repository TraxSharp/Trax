using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.Services.FeatureDbConfigurator;

/// <summary>
/// Configures a feature's own standalone <see cref="DbContext"/> for whichever database provider the host
/// chose. Each provider registration (<c>UsePostgres</c>/<c>UseSqlite</c>/<c>UseInMemory</c>) registers one of
/// these, so a higher-level feature that owns a separate context (e.g. state-machine snapshot persistence)
/// binds that context to the app's database without the host re-supplying a connection string.
///
/// <para>Deliberately NOT <c>IDataContextProviderFactory</c>: that factory is bound to the effect-journal
/// <c>IDataContext</c>, so a feature sharing it would share the effect layer's change tracker. This seam lets
/// a feature register a context on the same database but with its own tracker.</para>
/// </summary>
public interface ITraxFeatureDbConfigurator
{
    /// <summary>Apply the provider and connection the host configured to <paramref name="options"/>.</summary>
    void Configure(DbContextOptionsBuilder options);
}

/// <summary>
/// An <see cref="ITraxFeatureDbConfigurator"/> backed by a delegate, so each provider package supplies its own
/// <c>UseNpgsql</c>/<c>UseSqlite</c>/<c>UseInMemoryDatabase</c> call without the core package referencing any
/// provider.
/// </summary>
public sealed class DelegateFeatureDbConfigurator(Action<DbContextOptionsBuilder> configure)
    : ITraxFeatureDbConfigurator
{
    public void Configure(DbContextOptionsBuilder options) => configure(options);
}
