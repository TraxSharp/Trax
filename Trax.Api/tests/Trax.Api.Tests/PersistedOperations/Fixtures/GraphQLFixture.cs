using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Postgres.Utils;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests.PersistedOperations.Fixtures;

/// <summary>
/// Spins up a minimal Trax + GraphQL + PersistedOperations stack against the
/// real Postgres in <see cref="PostgresFixture"/>. Used by integration tests
/// that exercise the GraphQL mutations/queries the package extends onto the
/// root types.
/// </summary>
/// <remarks>
/// The schema is intentionally tiny (a single <c>hello</c> field) so the
/// tests focus on the persisted-operations surface rather than other Trax
/// GraphQL features. The schema is rebuilt per-test class but the underlying
/// Postgres state is wiped via <c>PostgresFixture.ClearAsync()</c> per-test.
/// </remarks>
public static class GraphQLFixture
{
    /// <summary>A document that validates against the test schema's <c>hello</c> field.</summary>
    public const string ValidDocument = "query Greet { hello }";

    /// <summary>A document referencing a field that does not exist on the schema.</summary>
    public const string SchemaMismatchDocument = "query Greet { nonexistentField }";

    /// <summary>A document with a parse error.</summary>
    public const string SyntaxErrorDocument = "query Greet { hello";

    /// <summary>A second valid document with the same response shape as ValidDocument.</summary>
    public const string ValidDocumentRewrite = "query Greet { hello # rewrite\n}";

    /// <summary>A valid document with a different response shape (extra field).</summary>
    public const string ShapeChangingDocument = "query Greet { hello version }";

    public static async Task<ServiceProvider> BuildAsync()
    {
        await DatabaseMigrator.Migrate(PostgresFixture.ConnectionString);
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddSingleton<TraxMarker>();
        sc.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        sc.AddSingleton(Substitute.For<IEffectRegistry>());
        sc.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(PostgresFixture.ConnectionString))
        );
        sc.AddTraxGraphQL(g =>
            g.ExposeOperationQueries()
                .ExposeOperationMutations()
                .AllowAnonymousOperations()
                .AddTypeExtension<HelloQuery>()
                .UsePersistedOperations(po => po.UseDatabase(PostgresFixture.ConnectionString))
        );
        return sc.BuildServiceProvider();
    }

    public static async Task<IRequestExecutor> GetExecutorAsync(
        IServiceProvider sp,
        CancellationToken ct = default
    )
    {
        var resolver = sp.GetRequiredService<IRequestExecutorProvider>();
        return await resolver.GetExecutorAsync("trax", ct);
    }

    [ExtendObjectType("RootQuery")]
    public class HelloQuery
    {
        public string Hello() => "world";

        public string Version() => "v1";
    }
}
