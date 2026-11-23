using System.ComponentModel.DataAnnotations.Schema;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

public class UnrestrictedBookDbContext : DbContext
{
    public DbSet<UnrestrictedBookRef> Books => Set<UnrestrictedBookRef>();

    public UnrestrictedBookDbContext(DbContextOptions<UnrestrictedBookDbContext> options)
        : base(options) { }

    public UnrestrictedBookDbContext() { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
            optionsBuilder.UseInMemoryDatabase("UnrestrictedBookDb_" + Guid.NewGuid());
    }
}
