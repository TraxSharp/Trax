using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

namespace Trax.Effect.Data.Testing.Tests;

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
