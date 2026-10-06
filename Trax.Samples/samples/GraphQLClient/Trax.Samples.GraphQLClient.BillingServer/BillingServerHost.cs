using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.GraphQLClient.BillingServer.Trains;

namespace Trax.Samples.GraphQLClient.BillingServer;

/// <summary>
/// Builds the billing GraphQL server. Factored out of <c>Program.cs</c> so the Gateway sample
/// can start it in-process alongside the inventory server, and the E2E suite can target it
/// through <c>WebApplicationFactory</c>. In-memory effects only — no database.
/// </summary>
public static class BillingServerHost
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
                        .ScanAssemblies(typeof(IGetInvoiceTrain).Assembly)
                        .AllowMissingAuthorizationService()
                )
        );

        // The outbound Trax GraphQL client reads this server's schema by introspection to validate
        // its queries. Trax serves introspection in Development only, which is where this demo
        // runs (the launch profile, the Gateway's launch profile, or WebApplicationFactory); in
        // Production the schema stays private and a client would load it from a file instead.
        builder.Services.AddTraxGraphQL();
        builder.Services.AddHealthChecks().AddTraxHealthCheck();

        var app = builder.Build();
        app.UseTraxGraphQL();
        app.MapHealthChecks("/trax/health");
        return app;
    }
}
