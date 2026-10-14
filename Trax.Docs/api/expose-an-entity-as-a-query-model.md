---
title: Expose an entity as a query model
description: "Serve an EF Core entity as a paged, filterable GraphQL query with TraxQueryModel: mark the entity, put it on a DbContext, register the context."
type: how-to
area: api
nav_order: 23
---

# Expose an entity as a query model

[`[TraxQueryModel]`](/docs/sdk-reference/graphql-api/query-models) serves an EF Core entity under
`discover` with paging, filtering, sorting and projection.

1. Mark your entity with `[TraxQueryModel]`:

```csharp compile=qs,domain
using Trax.Effect.Attributes;

[TraxAuthorize] // or [TraxAllowAnonymous]: without a posture the host refuses to start (see Authorization)
[TraxQueryModel(Description = "Player profiles")]
public class PlayerRecord
{
    public long Id { get; set; }
    public string PlayerId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Rating { get; set; }
}
```

2. Add the entity to a `DbSet<T>` on a `DbContext`:

```csharp compile=qs
using Microsoft.EntityFrameworkCore;

public class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<PlayerRecord> Players { get; set; } = null!;
}
```

3. Register the DbContext and enable model discovery:

```csharp compile=qs
using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.Extensions;

builder.Services.AddDbContextFactory<GameDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddTraxGraphQL(graphql =>
    graphql.AddDbContext<GameDbContext>());
```

For an application's own data, the base to use is a
[`DomainDataContext`](/docs/effect/effect-providers/domain-data-contexts) registered with
`AddDomainDataContext`, and it works here unchanged: `AddDbContext<T>()` names the concrete context
type, and the pooled factory `AddDomainDataContext` registers serves it.

```csharp compile=domain
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

public interface IGameDbContext
{
    DbSet<PlayerRecord> Players { get; }
}

public class GameDbContext(DbContextOptions<GameDbContext> options)
    : DomainDataContext<GameDbContext>(options), IGameDbContext
{
    public DbSet<PlayerRecord> Players => Set<PlayerRecord>();
    protected override string Schema => "game";
    protected override void ConfigureModel(ModelBuilder modelBuilder) { }
}
```

```csharp compile=domain
using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Services.DomainContext;

builder.Services.AddDomainDataContext<IGameDbContext, GameDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddTraxGraphQL(graphql => graphql.AddDbContext<GameDbContext>());
```

The [Bookworm sample](/docs/samples/bookworm) serves its catalog this way.

This generates a `playerRecords` query field under `discover`:

```graphql
query {
  discover {
    playerRecords(first: 10, where: { rating: { gte: 1500 } }, order: { rating: DESC }) {
      nodes {
        playerId
        displayName
        rating
      }
      pageInfo {
        hasNextPage
        endCursor
      }
      totalCount
    }
  }
}
```

## SDK Reference

> [TraxQueryModel](/docs/sdk-reference/graphql-api/query-models) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize)
