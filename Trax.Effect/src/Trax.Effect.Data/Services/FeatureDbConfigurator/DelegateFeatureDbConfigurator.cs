using System.ComponentModel;
using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.Services.FeatureDbConfigurator;

/// <summary>
/// An <see cref="ITraxFeatureDbConfigurator"/> backed by a delegate, so each provider package supplies its own
/// <c>UseNpgsql</c>/<c>UseSqlite</c>/<c>UseInMemoryDatabase</c> call without the core package referencing any
/// provider. Infrastructure registered by the provider packages; not intended for direct use.
/// </summary>
/// <param name="configure">The provider call to apply, such as <c>o =&gt; o.UseNpgsql(dataSource)</c>.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DelegateFeatureDbConfigurator(Action<DbContextOptionsBuilder> configure)
    : ITraxFeatureDbConfigurator
{
    /// <inheritdoc/>
    public void Configure(DbContextOptionsBuilder options) => configure(options);
}
