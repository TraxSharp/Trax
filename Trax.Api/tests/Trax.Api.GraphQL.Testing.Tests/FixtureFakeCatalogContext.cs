using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Core.Testing;

namespace Trax.Api.GraphQL.Testing.Tests;

public sealed class FixtureFakeCatalogContext : DbContext
{
    public DbSet<FixtureFakeBook> Books => Set<FixtureFakeBook>();
}
