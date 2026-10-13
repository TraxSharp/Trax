// What the compiled examples on reference/adopt-the-architecture-guards.md take as given: the
// application's own contexts, edge manifest, assembly marker and owner-scope types.
using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;

public sealed class CatalogDbContext;

public sealed class LendingDbContext;

public sealed class MyAssemblyMarker;

public static class MyCrossSchemaEdges
{
    public static IReadOnlyList<CrossSchemaEdge> All => [];
}

public interface IPrincipalAccessor;

public sealed class SystemPrincipal : IPrincipalAccessor;

public sealed class UserProfile;

public sealed class PollQuestionAnswer;

public sealed class ClientDbContext(
    DbContextOptions<ClientDbContext> options,
    IPrincipalAccessor principal
) : DbContext(options)
{
    public IPrincipalAccessor Principal { get; } = principal;
}
