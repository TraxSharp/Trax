using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

namespace Trax.Effect.Data.Testing.Tests;

// Fake domain contexts deriving the promoted DomainDataContext base, used to exercise the
// reflection guard (OneSchemaPerContext) against real EF models built offline.

public sealed class AlphaContext(DbContextOptions<AlphaContext> options)
    : DomainDataContext<AlphaContext>(options)
{
    protected override string Schema => "alpha";

    protected override void ConfigureModel(ModelBuilder modelBuilder) { }
}

public sealed class BetaContext(DbContextOptions<BetaContext> options)
    : DomainDataContext<BetaContext>(options)
{
    protected override string Schema => "beta";

    protected override void ConfigureModel(ModelBuilder modelBuilder) { }
}

public sealed class DuplicateSchemaContext(DbContextOptions<DuplicateSchemaContext> options)
    : DomainDataContext<DuplicateSchemaContext>(options)
{
    // Intentionally collides with AlphaContext's schema.
    protected override string Schema => "alpha";

    protected override void ConfigureModel(ModelBuilder modelBuilder) { }
}

public sealed class WidgetRow
{
    public int Id { get; set; }
}

// A context that maps an entity but ships no migration snapshot in this assembly, so its model has a
// table the (absent) snapshot does not: HasPendingModelChanges reports drift. Stands in for "someone
// changed the model without running `dotnet ef migrations add`".
public sealed class PendingChangesContext(DbContextOptions<PendingChangesContext> options)
    : DomainDataContext<PendingChangesContext>(options)
{
    public DbSet<WidgetRow> Widgets => Set<WidgetRow>();

    protected override string Schema => "pending";

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<WidgetRow>().HasKey(r => r.Id);
}
