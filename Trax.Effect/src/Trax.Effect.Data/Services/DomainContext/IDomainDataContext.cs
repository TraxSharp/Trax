namespace Trax.Effect.Data.Services.DomainContext;

/// <summary>
/// Minimal contract a domain data context exposes to application code (trains, junctions, services).
/// A domain declares its own <c>I{Domain}DataContext</c> deriving this interface and adding the owned
/// <c>DbSet&lt;T&gt;</c> properties (plus any cross-schema read accessors). Depending on the interface
/// rather than the concrete context keeps application logic testable.
/// </summary>
/// <remarks>
/// This is distinct from <see cref="Trax.Effect.Data.Services.DataContext.IDataContext"/>, which is
/// the framework's own effect/metadata store. A domain context is consumer data, registered alongside
/// the Trax effect layer via <c>AddDomainDataContext</c>.
/// </remarks>
public interface IDomainDataContext
{
    /// <summary>Persists pending changes. Mirrors <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
