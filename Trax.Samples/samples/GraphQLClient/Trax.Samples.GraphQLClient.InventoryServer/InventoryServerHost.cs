using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.GraphQLClient.InventoryServer.Trains;

namespace Trax.Samples.GraphQLClient.InventoryServer;

/// <summary>
/// Builds the inventory GraphQL server. Factored out of <c>Program.cs</c> so the Gateway
/// sample can start this server in-process alongside the billing server, and the E2E suite
/// can target it through <c>WebApplicationFactory</c>. In-memory effects only — no database.
/// </summary>
public static class InventoryServerHost
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // TrainAuthorizationService (registered by AddTraxGraphQL) depends on
        // IAuthorizationService. None of this server's trains are gated, but the host's DI
        // validation needs the authorization stack present.
        builder.Services.AddAuthorization();

        builder.Services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().AddJson())
                .AddMediator(mediator =>
                    mediator
                        .ScanAssemblies(typeof(IGetProductTrain).Assembly)
                        .AllowMissingAuthorizationService()
                )
        );

        // Introspection is enabled so an outbound Trax GraphQL client can fetch this server's
        // schema to validate queries. Demo only — keep introspection gated in production.
        builder.Services.AddTraxGraphQL(graphql => graphql.AllowIntrospection(_ => true));
        builder.Services.AddHealthChecks().AddTraxHealthCheck();

        var app = builder.Build();
        app.UseTraxGraphQL();
        app.MapHealthChecks("/trax/health");
        return app;
    }
}
