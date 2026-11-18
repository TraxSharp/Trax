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

public class BookDbContext : DbContext
{
    public DbSet<BookRef> Books { get; set; } = null!;

    public BookDbContext(DbContextOptions<BookDbContext> options)
        : base(options) { }

    public BookDbContext()
    {
        // Required for the manual instantiation paths some tests use.
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
            optionsBuilder.UseInMemoryDatabase("BookDb_" + Guid.NewGuid());
    }
}
