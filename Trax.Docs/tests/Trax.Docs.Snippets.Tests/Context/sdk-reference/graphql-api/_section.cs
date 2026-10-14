// What the examples under sdk-reference/graphql-api/ take as given: the Trax namespaces they
// use, and the game domain (a DbContext of players, its filter, sort and extension types) the
// builder examples register.
global using HotChocolate;
global using HotChocolate.Data.Filters;
global using HotChocolate.Data.Sorting;
global using HotChocolate.Execution.Configuration;
global using HotChocolate.Types;
global using Microsoft.EntityFrameworkCore;
global using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
global using Trax.Api.GraphQL.Extensions;
global using Trax.Core.Functional;
global using Trax.Core.Junction;
global using Trax.Effect.Attributes;
global using Trax.Effect.Data.Postgres.Extensions;
global using Trax.Effect.Extensions;
global using Trax.Effect.Services.ServiceTrain;
global using Trax.Mediator.Extensions;

public class Player
{
    public long Id { get; set; }

    public long TeamId { get; set; }

    public string DisplayName { get; set; } = "";

    public string MappedId { get; set; } = "";

    public string InternalMappedId { get; set; } = "";
}

public class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
}

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options);

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

public class PlayerFilterInputType : FilterInputType<Player>;

public class PlayerSortInputType : SortInputType<Player>;

public class RelationshipTypeModule : TypeModule
{
    public override ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        HotChocolate.Types.Descriptors.IDescriptorContext context,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<IReadOnlyCollection<ITypeSystemMember>>([]);
}

[ExtendObjectType(typeof(Player))]
public class PlayerStatsExtension
{
    [TraxAuthorize]
    public int GetWins([Parent] Player player) => 0;
}
