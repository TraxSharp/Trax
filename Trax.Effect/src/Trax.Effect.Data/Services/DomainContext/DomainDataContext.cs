using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Extensions;

namespace Trax.Effect.Data.Services.DomainContext;

/// <summary>
/// Base class for a consumer's domain data context. Encodes the recommended data-layer rule:
/// <b>one project : one schema : one context</b>.
/// </summary>
/// <typeparam name="TSelf">The concrete context type (curiously-recurring), e.g. <c>CatalogDbContext</c>.</typeparam>
/// <remarks>
/// A domain context derives this, declares its single <see cref="Schema"/>, and configures its owned
/// entities in <see cref="ConfigureModel"/>. The base seals <see cref="OnModelCreating"/> so the two
/// cross-cutting conventions (default schema + UTC datetime handling) can never be forgotten or
/// reordered:
/// <list type="number">
///   <item>The default schema is applied on providers that support schemas (PostgreSQL). It is
///   skipped on schema-less providers (SQLite, the EF in-memory provider) so the same context class
///   works across the provider matrix used by tests.</item>
///   <item><see cref="ConfigureModel"/> runs the domain's own mapping, including any cross-schema
///   reads wired via an entity's static <c>OnCrossSchemaModelCreating(ModelBuilder, string)</c>.</item>
///   <item>The UTC datetime converter is applied last, over the fully-built model.</item>
/// </list>
/// This base is deliberately <b>not</b>
/// <see cref="Trax.Effect.Data.Services.DataContext.DataContext{TDbContext}"/>: that type is the
/// framework's own metadata store (it carries the <c>trax</c> tables and the effect-provider
/// contract). A domain context is a separate, plain EF context registered alongside the Trax effect
/// layer via <c>AddDomainDataContext</c>.
/// </remarks>
public abstract class DomainDataContext<TSelf>(DbContextOptions<TSelf> options)
    : DbContext(options),
        IDomainDataContext
    where TSelf : DbContext
{
    /// <summary>
    /// The single database schema this context owns. Every owned table lands here. Provide a value
    /// from a shared schema-name constants class rather than a string literal.
    /// </summary>
    protected abstract string Schema { get; }

    /// <summary>
    /// Configures the owned entity model (keys, indexes, relationships) and any cross-schema reads.
    /// Called by the sealed <see cref="OnModelCreating"/>; do not call <c>HasDefaultSchema</c> here,
    /// the base owns that.
    /// </summary>
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    /// <summary>
    /// Returns this context typed as the concrete <typeparamref name="TSelf"/>, for code holding the
    /// companion interface that needs an EF member the interface does not surface.
    /// </summary>
    public TSelf Raw() => (TSelf)(object)this;

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        if (SupportsSchemas)
            modelBuilder.HasDefaultSchema(Schema);

        ConfigureModel(modelBuilder);

        modelBuilder.ApplyUtcDateTimeConverter();
    }

    // PostgreSQL is the only schema-aware provider Trax targets. SQLite and the in-memory provider
    // have no schema concept, so applying a default schema there would error or be silently
    // meaningless. Probe by provider name to avoid taking a hard Npgsql dependency in the base.
    private bool SupportsSchemas =>
        Database.ProviderName is { } provider
        && provider.Contains("Npgsql", StringComparison.Ordinal);
}
