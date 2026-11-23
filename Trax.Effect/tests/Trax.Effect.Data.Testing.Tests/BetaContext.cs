using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

namespace Trax.Effect.Data.Testing.Tests;

public sealed class BetaContext(DbContextOptions<BetaContext> options)
    : DomainDataContext<BetaContext>(options)
{
    protected override string Schema => "beta";

    protected override void ConfigureModel(ModelBuilder modelBuilder) { }
}
