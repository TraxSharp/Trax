using System.Reflection;
using System.Text;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// A TestServer host for the typed machine surface's schema tests: Trax with an in-memory store, exactly the machines a
/// test names registered as <see cref="IMachine"/> (each fake is marked <c>[TraxMachine]</c>, so each is served), no
/// assembly scanned for machines unless a test passes one, a stubbed train list
/// holding one anonymous query train, and an API key per role. Starting it runs every startup check; reading its
/// executor builds the schema.
/// </summary>
internal static class MachineSchemaHost
{
    public const string UserKey = "machines-schema-user";
    public const string ApproverKey = "machines-schema-approver";

    /// <summary>The schema's root type names.</summary>
    public const string RootQuery = "RootQuery";

    public const string RootMutation = "RootMutation";

    /// <summary>
    /// Starts the host. <paramref name="machines"/> are the machine types it registers; <paramref name="trains"/>
    /// replaces the train list (the anonymous query train is always added); <paramref name="scan"/> is the one
    /// assembly <c>AddStateMachines</c> scans, when a test gives one.
    /// </summary>
    public static async Task<IHost> StartAsync(
        IReadOnlyList<Type> machines,
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder>? graphql = null,
        IReadOnlyList<TrainRegistration>? trains = null,
        Action<IServiceCollection>? services = null,
        bool persistedOperations = true,
        Assembly? scan = null
    )
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(s =>
                    {
                        s.AddLogging();
                        s.AddRouting();
                        s.AddTraxApiKeyAuth(keys =>
                            keys.Add(UserKey, id: "user")
                                .Add(ApproverKey, id: "approver", "Approver")
                        );
                        s.AddAuthorization();
                        s.AddTrax(trax =>
                            trax.AddEffects(e => e.UseInMemory())
                                .AddStateMachines(scan is null ? [] : [scan])
                        );
                        foreach (var machine in machines)
                            s.AddSingleton(typeof(IMachine), machine);
                        s.AddScoped<ISnapshotPrincipal, NoPrincipal>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([PingTrain(), .. trains ?? []]);
                        s.AddSingleton(discovery);
                        s.AddSingleton(Substitute.For<ITraxScheduler>());
                        s.AddSingleton(Substitute.For<IOperationsService>());
                        s.AddSingleton(Substitute.For<ITrainExecutionService>());
                        s.AddSingleton(Substitute.For<ITraxHealthService>());
                        s.AddSingleton(Substitute.For<IJobSubmitter>());
                        services?.Invoke(s);
                        s.AddTraxGraphQL(g =>
                        {
                            var built = (graphql ?? (x => x))(g);
                            // Served machines require persisted operations; a test that configures them itself
                            // keeps its own configuration.
                            return
                                !persistedOperations
                                || built.Services.Any(d =>
                                    d.ServiceType == typeof(IPersistedOperationsCapability)
                                )
                                ? built
                                : built.UsePersistedOperations(po =>
                                    po.SingleNode()
                                        .RequirePersisted(false)
                                        .LogNonPersistedRequests(true)
                                );
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .Build();

        try
        {
            await host.StartAsync();
        }
        catch
        {
            host.Dispose();
            throw;
        }
        return host;
    }

    /// <summary>Starts the host and builds its schema; for the tests that read the schema.</summary>
    public static async Task<(IHost Host, IRequestExecutor Executor)> StartWithSchemaAsync(
        IReadOnlyList<Type> machines,
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder>? graphql = null,
        IReadOnlyList<TrainRegistration>? trains = null
    )
    {
        var host = await StartAsync(machines, graphql, trains);
        try
        {
            return (host, await ExecutorAsync(host));
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    /// <summary>
    /// What refuses a host that registers <paramref name="machines"/>: the configuration, a startup check, or the
    /// schema build. Null when the host starts and its schema builds.
    /// </summary>
    public static async Task<Exception?> RefusalAsync(
        IReadOnlyList<Type> machines,
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder>? graphql = null,
        IReadOnlyList<TrainRegistration>? trains = null,
        bool persistedOperations = true,
        Assembly? scan = null,
        Action<IServiceCollection>? services = null
    )
    {
        try
        {
            using var host = await StartAsync(
                machines,
                graphql,
                trains,
                services,
                persistedOperations: persistedOperations,
                scan: scan
            );
            await ExecutorAsync(host);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    public static Task<IRequestExecutor> ExecutorAsync(IHost host) =>
        host
            .Services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax")
            .AsTask();

    /// <summary>The object type named <paramref name="name"/>, or null.</summary>
    public static ObjectType? ObjectTypeNamed(IRequestExecutor executor, string name) =>
        executor.Schema.Types.OfType<ObjectType>().SingleOrDefault(t => t.Name == name);

    /// <summary>
    /// The type <c>machines.&lt;machine&gt;</c> resolves to under <paramref name="root"/>, or null when there is no
    /// such field.
    /// </summary>
    public static ObjectType? MachineType(IRequestExecutor executor, string root, string machine)
    {
        var machines = FieldType(ObjectTypeNamed(executor, root), "machines");
        return FieldType(machines, machine);
    }

    /// <summary>The object type <paramref name="field"/> of <paramref name="type"/> resolves to, or null.</summary>
    public static ObjectType? FieldType(ObjectType? type, string field) =>
        type is not null && type.Fields.TryGetField(field, out var f)
            ? f.Type.NamedType() as ObjectType
            : null;

    /// <summary>The named types reachable from <paramref name="start"/>, through fields and their arguments.</summary>
    public static IReadOnlyCollection<ITypeDefinition> Reachable(ObjectType start)
    {
        var seen = new Dictionary<string, ITypeDefinition>(StringComparer.Ordinal);
        var queue = new Queue<ITypeDefinition>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (!seen.TryAdd(type.Name, type))
                continue;
            switch (type)
            {
                case ObjectType o:
                    foreach (var f in o.Fields.Where(f => !f.IsIntrospectionField))
                    {
                        queue.Enqueue(f.Type.NamedType());
                        foreach (var a in f.Arguments)
                            queue.Enqueue(a.Type.NamedType());
                    }
                    break;
                case InputObjectType i:
                    foreach (var f in i.Fields)
                        queue.Enqueue(f.Type.NamedType());
                    break;
            }
        }
        return seen.Values;
    }

    public static async Task<string> PostMachinesAsync(
        this IHost host,
        string query,
        string? apiKey
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { query }),
                Encoding.UTF8,
                "application/json"
            ),
        };
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);

        var response = await host.GetTestClient().SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>A train registration for the schema only: nothing runs it.</summary>
    public static TrainRegistration Train<TService, TInput, TOutput>(
        bool isQuery,
        string? ns = null
    ) =>
        new()
        {
            ServiceType = typeof(TService),
            ImplementationType = typeof(object),
            InputType = typeof(TInput),
            OutputType = typeof(TOutput),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = typeof(TService).Name,
            ImplementationTypeName = typeof(TService).Name[1..],
            HasAllowAnonymousAttribute = true,
            InputTypeName = typeof(TInput).Name,
            OutputTypeName = typeof(TOutput).Name,
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = isQuery,
            IsMutation = !isQuery,
            IsRemote = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = GraphQLOperation.Run,
            GraphQLNamespace = ns,
        };

    private static TrainRegistration PingTrain() =>
        Train<IMachineSchemaPingTrain, MachineSchemaPingInput, Unit>(isQuery: true);

    private interface IMachineSchemaPingTrain;

    public sealed record MachineSchemaPingInput
    {
        public string Value { get; init; } = "";
    }

    private sealed class NoPrincipal : ISnapshotPrincipal
    {
        public string? CurrentUserKey => null;
    }
}
