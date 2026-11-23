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

public class InMemoryProbeDbContext : DbContext
{
    public DbSet<UnindexedRow> Unindexed => Set<UnindexedRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseInMemoryDatabase("IndexProbe_" + Guid.NewGuid());
}
