using System.Collections.Concurrent;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Effect.Attributes;
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

    public static Task<ServiceProvider> BuildAsync() => BuildAsync(po => po.SingleNode());

    /// <summary>
    /// Builds the stack with <paramref name="topology"/>, then lets <paramref name="services"/>
    /// add or replace registrations (a fake clock, a broadcaster that fails) before the provider
    /// is built.
    /// </summary>
    public static Task<ServiceProvider> BuildAsync(
        Func<
            Trax.Api.GraphQL.PersistedOperations.Configuration.PersistedOperationsBuilder,
            Trax.Api.GraphQL.PersistedOperations.Configuration.PersistedOperationsBuilder
        > topology,
        Action<IServiceCollection> services
    ) => BuildCoreAsync(topology, services);

    /// <summary>
    /// Builds the stack with the persisted-operations options <paramref name="topology"/> adds
    /// to the database connection: <c>SingleNode()</c> or <c>UseRabbitMqInvalidation(...)</c>.
    /// </summary>
    public static Task<ServiceProvider> BuildAsync(
        Func<
            Trax.Api.GraphQL.PersistedOperations.Configuration.PersistedOperationsBuilder,
            Trax.Api.GraphQL.PersistedOperations.Configuration.PersistedOperationsBuilder
        > topology
    ) => BuildCoreAsync(topology, null);

    private static async Task<ServiceProvider> BuildCoreAsync(
        Func<
            Trax.Api.GraphQL.PersistedOperations.Configuration.PersistedOperationsBuilder,
            Trax.Api.GraphQL.PersistedOperations.Configuration.PersistedOperationsBuilder
        > topology,
        Action<IServiceCollection>? services
    )
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
                .UsePersistedOperations(po => topology(po))
        );
        services?.Invoke(sc);
        return sc.BuildServiceProvider();
    }

    private static readonly ConcurrentDictionary<string, HeldRequest> s_held = new();

    /// <summary>
    /// A document whose <c>held</c> field does not return until <see cref="HeldRequest.Release"/>
    /// is called for <paramref name="gate"/>, so a test can change the store while a request that
    /// already read it is still running.
    /// </summary>
    public static string HeldDocument(string gate) => $"query Held {{ held(gate: \"{gate}\") }}";

    /// <summary>Opens the gate a <see cref="HeldDocument"/> request waits on.</summary>
    public static HeldRequest Hold(string gate) => s_held.GetOrAdd(gate, _ => new HeldRequest());

    /// <summary>The two sides of a held request: it has entered the resolver, and it may leave.</summary>
    public sealed class HeldRequest
    {
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _released = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        /// <summary>Completes once the resolver is running.</summary>
        public Task Entered => _entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        /// <summary>Lets the resolver return.</summary>
        public void Release() => _released.TrySetResult();

        internal async Task<string> RunAsync()
        {
            _entered.TrySetResult();
            await _released.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return "held";
        }
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
        // The fixture endpoint is open by design (it asserts persisted-operation enforcement,
        // not authorization), so both fields say so rather than inheriting a gate that is not
        // there.
        [TraxAllowAnonymous]
        public string Hello() => "world";

        [TraxAllowAnonymous]
        public string Version() => "v1";

        [TraxAllowAnonymous]
        public Task<string> Held(string gate) => Hold(gate).RunAsync();
    }
}
