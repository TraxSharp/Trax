using HotChocolate.Data;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Errors;
using Trax.Api.GraphQL.Hooks;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.GraphQL.TypeModules;
using Trax.Api.GraphQL.Types;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Api.GraphQL.Extensions;

public static class GraphQLServiceExtensions
{
    private const string SchemaName = "trax";

    /// <summary>
    /// Registers the Trax GraphQL schema on a named HotChocolate server ("trax")
    /// with support for configuring DbContext-based model queries.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddTraxGraphQL(graphql => graphql
    ///     .AddDbContext&lt;GameDbContext&gt;());
    /// </code>
    /// </example>
    public static IServiceCollection AddTraxGraphQL(
        this IServiceCollection services,
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder> configure
    )
    {
        if (!services.Any(sd => sd.ServiceType == typeof(TraxMarker)))
            throw new InvalidOperationException(
                "AddTraxGraphQL() requires AddTrax() to be called first. "
                    + "Call services.AddTrax(trax => ...) before services.AddTraxGraphQL()."
            );

        var builder = new TraxGraphQLBuilder(services);
        configure(builder);
        var config = builder.Build();
        services.AddSingleton(config);

        services.AddTraxApi();
        services.AddSingleton<TrainTypeModule>();
        services.AddTransient<GraphQLSubscriptionHook>();
        services
            .AddSingleton<LifecycleHookFactory<GraphQLSubscriptionHook>>()
            .AddSingleton<ITrainLifecycleHookFactory>(sp =>
                sp.GetRequiredService<LifecycleHookFactory<GraphQLSubscriptionHook>>()
            );

        var graphqlBuilder = services
            .AddGraphQLServer(SchemaName)
            .AddQueryType<RootQuery>()
            .AddMutationType<RootMutation>()
            .AddSubscriptionType<LifecycleSubscriptions>()
            .AddType<ObjectType<OperationsMutations>>()
            .AddType<ObjectType<OperationsQueries>>()
            .AddType<TrainLifecycleEventType>()
            .AddTypeModule<TrainTypeModule>()
            .AddErrorFilter<TraxErrorFilter>()
            .AddInMemorySubscriptions();

        if (config.ModelRegistrations.Count > 0)
        {
            services.AddSingleton<QueryModelTypeModule>();
            graphqlBuilder.AddTypeModule<QueryModelTypeModule>();

            // Register DiscoverQueries base type and discover field on RootQuery.
            // TrainTypeModule will skip creating these when it detects model registrations.
            graphqlBuilder.AddType(new ObjectType<DiscoverQueries>());
            graphqlBuilder.AddTypeExtension(
                new ObjectTypeExtension(d =>
                {
                    d.Name("RootQuery");
                    d.Field("discover")
                        .Type<ObjectType<DiscoverQueries>>()
                        .Resolve(_ => new DiscoverQueries());
                })
            );

            if (config.ModelRegistrations.Any(r => r.Attribute.Filtering))
                graphqlBuilder.AddFiltering();

            if (config.ModelRegistrations.Any(r => r.Attribute.Sorting))
                graphqlBuilder.AddSorting();

            if (config.ModelRegistrations.Any(r => r.Attribute.Projection))
                graphqlBuilder.AddProjections();
        }

        // If a broadcaster receiver is registered (via UseBroadcaster()),
        // wire up the GraphQL handler so remote lifecycle events are forwarded
        // to HotChocolate subscriptions.
        if (services.Any(sd => sd.ServiceType == typeof(ITrainEventReceiver)))
        {
            services.AddTransient<ITrainEventHandler, GraphQLTrainEventHandler>();
        }

        return services;
    }

    /// <summary>
    /// Registers the Trax GraphQL schema on a named HotChocolate server ("trax").
    /// This avoids conflicts with a consumer's own default GraphQL schema.
    /// Only trains annotated with <c>[TraxQuery]</c> or <c>[TraxMutation]</c> get typed operations generated.
    /// </summary>
    public static IServiceCollection AddTraxGraphQL(this IServiceCollection services) =>
        services.AddTraxGraphQL(builder => builder);

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
