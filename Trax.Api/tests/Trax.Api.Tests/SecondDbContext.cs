using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using AwesomeAssertions;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

public class SecondDbContext : DbContext
{
    public DbSet<TestItem> Items => Set<TestItem>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase("SecondDb_" + Guid.NewGuid());
}
