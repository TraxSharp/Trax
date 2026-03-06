using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Hooks;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Api.GraphQL.Extensions;

public static class GraphQLServiceExtensions
{
    private const string SchemaName = "trax";

    /// <summary>
    /// Registers the Trax GraphQL schema on a named HotChocolate server ("trax").
    /// This avoids conflicts with a consumer's own default GraphQL schema.
    /// Only trains annotated with <c>[TraxQuery]</c> or <c>[TraxMutation]</c> get typed operations generated.
    /// </summary>
    public static IServiceCollection AddTraxGraphQL(this IServiceCollection services)
    {
        if (!services.Any(sd => sd.ServiceType == typeof(TraxMarker)))
            throw new InvalidOperationException(
                "AddTraxGraphQL() requires AddTrax() to be called first. "
                    + "Call services.AddTrax(trax => ...) before services.AddTraxGraphQL()."
            );

        services.AddTraxApi();
        services.AddSingleton<TrainTypeModule>();
        services.AddTransient<GraphQLSubscriptionHook>();
        services
            .AddSingleton<GraphQLSubscriptionHookFactory>()
            .AddSingleton<ITrainLifecycleHookFactory>(sp =>
                sp.GetRequiredService<GraphQLSubscriptionHookFactory>()
            );
        services
            .AddGraphQLServer(SchemaName)
            .AddQueryType<RootQuery>()
            .AddMutationType<RootMutation>()
            .AddSubscriptionType<LifecycleSubscriptions>()
            .AddType<ObjectType<DispatchMutations>>()
            .AddType<ObjectType<OperationsMutations>>()
            .AddType<ObjectType<DiscoverQueries>>()
            .AddType<ObjectType<OperationsQueries>>()
            .AddTypeModule<TrainTypeModule>()
            .AddInMemorySubscriptions();

        return services;
    }

    /// <summary>
    /// Maps the Trax GraphQL endpoint at the specified route prefix.
    /// Uses a named schema so it coexists with other HotChocolate schemas
    /// in the same application. Use the optional <paramref name="configure"/> callback
    /// to apply endpoint conventions such as authorization or rate limiting.
    /// </summary>
    /// <example>
    /// <code>
    /// app.UseTraxGraphQL(configure: endpoint => endpoint
    ///     .RequireAuthorization("AdminPolicy"));
    /// </code>
    /// </example>
    public static WebApplication UseTraxGraphQL(
        this WebApplication app,
        string routePrefix = "/trax/graphql",
        Action<IEndpointConventionBuilder>? configure = null
    )
    {
        app.UseWebSockets();
        var endpoint = app.MapGraphQL(routePrefix, SchemaName);
        configure?.Invoke(endpoint);
        return app;
    }
}
