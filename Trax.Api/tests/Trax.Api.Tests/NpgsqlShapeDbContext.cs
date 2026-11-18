using System.Text.RegularExpressions;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Data;
using HotChocolate.Data.Filters;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Filtering.ListElements;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.TypeModules;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// A real Npgsql model used only to compile queries and read back the SQL. It never
/// opens a connection, so it needs no database.
/// </summary>
public class NpgsqlShapeDbContext : DbContext
{
    public DbSet<ShapeRow> Rows => Set<ShapeRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseNpgsql(
            "Host=localhost;Database=shape;Username=shape;Password=shape",
            npgsql => npgsql.MapEnum<Badge>("badge")
        );

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<Badge>();
        // Declared on Badges (an array) and LegacyTags (an IList<T>) but deliberately not
        // on Tags or Scores, so the tests can compare the two translations.
        modelBuilder.Entity<ShapeRow>().HasIndex(r => r.Badges).HasMethod("gin");
        modelBuilder.Entity<ShapeRow>().HasIndex(r => r.LegacyTags).HasMethod("gin");
    }
}
