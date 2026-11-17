using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

namespace Trax.Effect.Data.Testing.Tests;

public sealed class DuplicateSchemaContext(DbContextOptions<DuplicateSchemaContext> options)
    : DomainDataContext<DuplicateSchemaContext>(options)
{
    // Intentionally collides with AlphaContext's schema.
    protected override string Schema => "alpha";

    protected override void ConfigureModel(ModelBuilder modelBuilder) { }
}
