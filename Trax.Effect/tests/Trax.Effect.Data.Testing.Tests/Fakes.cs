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
