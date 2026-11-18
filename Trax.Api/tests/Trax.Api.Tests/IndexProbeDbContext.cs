using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Data.Filters;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.TypeModules;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

public class IndexProbeDbContext : DbContext
{
    public DbSet<IndexedRow> Indexed => Set<IndexedRow>();
    public DbSet<UnindexedRow> Unindexed => Set<UnindexedRow>();
    public DbSet<ChapterRow> Chapters => Set<ChapterRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseNpgsql("Host=localhost;Database=probe;Username=probe;Password=probe");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IndexedRow>().HasIndex(r => r.Tight).HasMethod("gin");
        // Indexed, but not with a method that can serve array containment.
        modelBuilder.Entity<IndexedRow>().HasIndex(r => r.Btree);
    }
}
