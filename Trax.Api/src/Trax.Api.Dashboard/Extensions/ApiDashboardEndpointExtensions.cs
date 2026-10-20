using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Api.Dashboard.Configuration;
using Trax.Api.Dashboard.Serving;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.Dashboard.Extensions;

/// <summary>
/// Serves the Trax operations dashboard, the single-page app this package carries, from the host.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/api-dashboard/use-trax-api-dashboard">SDK reference</seealso>
public static class ApiDashboardEndpointExtensions
{
    /// <summary>The path the dashboard is served at unless another is given.</summary>
    private const string DefaultPath = "/trax/dashboard";

    /// <summary>The GraphQL path the app talks to when nothing else names one.</summary>
    private const string DefaultGraphQLPath = "/trax/graphql";

    /// <summary>
    /// Serves the dashboard at <paramref name="path"/>, behind the authorization posture chosen in
    /// <paramref name="configure"/>. The app is built into this package; the host needs no Node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The app reads and changes everything through the host's Trax GraphQL endpoint, so it needs
    /// <c>AddTraxGraphQL()</c>, and talks to the path <c>UseTraxGraphQL()</c> mapped (or
    /// <see cref="TraxApiDashboardOptions.GraphQLPath"/>). It authenticates there with the API key
    /// or bearer token the operator enters, which that endpoint authorizes.
    /// </para>
    /// <para>
    /// Every response under the dashboard's path, refusals included, carries a strict
    /// Content-Security-Policy, <c>nosniff</c>, <c>Referrer-Policy: no-referrer</c> and no
    /// framing. Any path under the dashboard that is not one of its files is a route of the app
    /// and gets its page; a missing file is a 404.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The host's endpoints, such as the built <c>WebApplication</c>.</param>
    /// <param name="configure">
    /// Chooses who may load the app: <see cref="TraxApiDashboardOptions.RequirePolicy"/>,
    /// <see cref="TraxApiDashboardOptions.RequireRoles"/> or
    /// <see cref="TraxApiDashboardOptions.AllowAnonymousShell"/>. Required.
    /// </param>
    /// <param name="path">The path to serve it at. Defaults to <c>/trax/dashboard</c>.</param>
    /// <returns>
    /// The convention builder for the dashboard's endpoint, so the host can add conventions of its
    /// own (<c>RequireHost</c>, rate limiting, a further <c>RequireAuthorization</c>). They add to
    /// the posture; they do not replace it, and <c>AllowAnonymous()</c>, which would, makes a
    /// policy or roles posture fail at startup.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// No posture was chosen; the named policy is not registered; a policy or roles posture has
    /// no authentication scheme to challenge a caller with; <c>AddTraxGraphQL()</c> was not
    /// called, or does not expose the operations namespace (<c>ExposeOperationQueries()</c>); or
    /// this build of the package carries no app; or <c>AllowAnonymous()</c> was added to a policy
    /// or roles posture (thrown when the host starts).
    /// </exception>
    public static IEndpointConventionBuilder UseTraxApiDashboard(
        this IEndpointRouteBuilder endpoints,
        Action<TraxApiDashboardOptions> configure,
        string path = DefaultPath
    )
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configure);
        var services = endpoints.ServiceProvider;

        var options = new TraxApiDashboardOptions();
        configure(options);
        var routePrefix = NormalizePath(path);
        if (options.GraphQLPath is not null && !IsLocalPath(options.GraphQLPath))
            throw new ArgumentException(
                $"GraphQLPath '{options.GraphQLPath}' is not a path on this host. Give the path "
                    + "the Trax GraphQL endpoint is mapped at, such as /trax/graphql.",
                nameof(configure)
            );

        var graphQL =
            services.GetService<GraphQLConfiguration>()
            ?? throw new InvalidOperationException(
                "UseTraxApiDashboard() requires AddTraxGraphQL(): the dashboard reads and changes "
                    + "everything through the Trax GraphQL endpoint. Call "
                    + "builder.Services.AddTraxGraphQL(...) before builder.Build(), and map it with "
                    + "app.UseTraxGraphQL()."
            );
        if (!graphQL.OperationQueriesExposed)
            throw new InvalidOperationException(
                "UseTraxApiDashboard() requires the GraphQL operations namespace, which every page "
                    + "of the dashboard reads, and AddTraxGraphQL() does not expose it. Call "
                    + "ExposeOperationQueries() on the GraphQL builder, and "
                    + "ExposeOperationMutations() for the dashboard's actions, with the gate the "
                    + "builder then asks for (RequireAuthorization() or GateOperations(...)). See "
                    + "https://traxsharp.net/docs/cross-cutting/troubleshooting#usetraxapidashboard-requires-the-graphql-operations-namespace"
            );

        var assets = services.GetService<DashboardAssets>() ?? DashboardAssets.Embedded;
        if (assets.IndexTemplate is null)
            throw new InvalidOperationException(
                "This build of Trax.Api.Dashboard carries no app. The package from nuget.org always "
                    + "does; a build from the Trax repository embeds it only when "
                    + "Trax.Api/src/Trax.Api.Dashboard/ClientApp has its npm packages. Run "
                    + "`npm ci --ignore-scripts` there and build again."
            );
        // Fails here, at startup, rather than on the first request.
        DashboardEndpoint.RenderIndex(
            assets.IndexTemplate,
            routePrefix,
            DefaultGraphQLPath,
            new Dictionary<string, string>()
        );

        // The documents the app sends, for a host that runs persisted operations to register when it starts; it hands
        // back the id of each, which the app is told in its runtime config.
        graphQL.ConsoleDocuments = ConsoleDocumentsResource.Documents;

        VerifyAuthorizationPosture(services, options);

        var endpoint = new DashboardEndpoint(
            assets,
            () => options.GraphQLPath ?? graphQL.MappedRoutePrefix ?? DefaultGraphQLPath,
            () => graphQL.ConsoleDocumentIds,
            options.AnonymousShellAllowed,
            options.HostAuthenticated
        );
        var marker = new DashboardEndpointMetadata(options.AnonymousShellAllowed);
        DashboardResponseHeaders.Subscribe(
            services.GetService<DiagnosticListener>()
                ?? throw new InvalidOperationException(
                    "UseTraxApiDashboard() needs the host's DiagnosticListener, which ASP.NET Core "
                        + "registers, to put its security headers on the refusals the "
                        + "authorization middleware answers. Map it on an ASP.NET Core host."
                ),
            marker
        );

        // Every method, so a method the app does not serve is answered by the endpoint (405, with
        // the security headers) rather than by routing.
        var builder = endpoints
            .Map($"{routePrefix}/{{**{DashboardEndpoint.PathRouteValue}}}", endpoint.HandleAsync)
            .WithDisplayName($"Trax API dashboard ({routePrefix})")
            .WithMetadata(marker);
        RefuseAllowAnonymousOverAPosture(endpoints, builder, marker, routePrefix);

        if (options.AnonymousShellAllowed)
        {
            services
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Trax.Api.Dashboard")
                .LogWarning(
                    "AllowAnonymousShell() is set: the Trax API dashboard at {Path} applies no "
                        + "authorization of its own, so anyone who can reach it can load the app. "
                        + "What the app shows and changes is still authorized by the Trax GraphQL "
                        + "endpoint. Use RequirePolicy() or RequireRoles() unless something in "
                        + "front of it is the gate.",
                    routePrefix
                );
            return builder;
        }

        builder.RequireAuthorization(
            new AuthorizeAttribute
            {
                Policy = options.Policy,
                Roles = options.Roles is null ? null : string.Join(",", options.Roles),
            }
        );
        return builder;
    }

    // [AllowAnonymous] on the endpoint makes the authorization middleware skip every policy on it,
    // so a host chaining AllowAnonymous() on the returned builder would remove the posture without
    // a word. The convention runs when the endpoint is built, after every convention the host
    // added; a WebApplication builds the dashboard's endpoints while it starts, so the refusal is
    // a startup failure. Elsewhere it surfaces when routing first builds its endpoints.
    private static void RefuseAllowAnonymousOverAPosture(
        IEndpointRouteBuilder endpoints,
        IEndpointConventionBuilder builder,
        DashboardEndpointMetadata marker,
        string routePrefix
    )
    {
        if (marker.AnonymousShell)
            return;

        builder.Finally(endpoint =>
        {
            if (endpoint.Metadata.Any(m => m is IAllowAnonymous))
                throw new InvalidOperationException(
                    $"The Trax API dashboard at {routePrefix} was given AllowAnonymous(), which "
                        + "removes its RequirePolicy()/RequireRoles() posture instead of adding to "
                        + "it. To serve the app without authorization of its own, use "
                        + "o => o.AllowAnonymousShell() in UseTraxApiDashboard() and remove "
                        + "AllowAnonymous()."
                );
        });

        if (endpoints is IApplicationBuilder app)
            app.Use(next =>
            {
                // Runs once, while the host builds its pipeline at start, after every convention.
                foreach (var source in endpoints.DataSources)
                    _ = source.Endpoints.Count;
                return next;
            });
    }

    private static string NormalizePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var trimmed = "/" + path.Trim().Trim('/');
        if (trimmed == "/" || !IsLocalPath(trimmed) || trimmed.Contains('{'))
            throw new ArgumentException(
                $"'{path}' cannot be the dashboard's path: it needs a literal path of its own, "
                    + "such as /trax/dashboard, because everything under it is the app's.",
                nameof(path)
            );
        return trimmed;
    }

    private static bool IsLocalPath(string path) =>
        path.StartsWith('/')
        && !path.StartsWith("//", StringComparison.Ordinal)
        && !path.Contains('\\')
        && !path.Contains("://", StringComparison.Ordinal);

    private static void VerifyAuthorizationPosture(
        IServiceProvider services,
        TraxApiDashboardOptions options
    )
    {
        if (!options.HasAuthorizationPosture)
            throw new InvalidOperationException(
                "UseTraxApiDashboard() needs to know who may load the dashboard, an admin UI for "
                    + "queueing, running and cancelling trains. Choose one: "
                    + "o => o.RequirePolicy(\"<policy>\"), o => o.RequireRoles(\"<role>\"), or "
                    + "o => o.AllowAnonymousShell() when something in front of it (a fallback "
                    + "policy, an ingress rule) is the gate. The data it shows is authorized by "
                    + "the Trax GraphQL endpoint either way."
            );

        if (options.AnonymousShellAllowed)
            return;

        var policies =
            services.GetService<IAuthorizationPolicyProvider>()
            ?? throw new InvalidOperationException(
                "The dashboard requires authorization, and the host has no authorization services. "
                    + "Call builder.Services.AddAuthorization(...)."
            );
        var policy = options.Policy is null
            ? null
            : policies.GetPolicyAsync(options.Policy).GetAwaiter().GetResult();
        if (options.Policy is not null && policy is null)
            throw new InvalidOperationException(
                $"The dashboard requires the authorization policy '{options.Policy}', which is "
                    + "not registered. Add it with builder.Services.AddAuthorization(o => "
                    + $"o.AddPolicy(\"{options.Policy}\", ...))."
            );

        VerifyChallengeScheme(services, policy);
    }

    // A refused request is answered by challenging (or forbidding) through an authentication
    // scheme: the ones the policy names, or else the host's default. With none, ASP.NET Core
    // throws on every request the posture refuses, so the dashboard would answer 500 instead of
    // 401 or 403. Refuse at startup instead, naming what is missing.
    private static void VerifyChallengeScheme(
        IServiceProvider services,
        AuthorizationPolicy? policy
    )
    {
        var schemes = services.GetService<IAuthenticationSchemeProvider>();
        if (policy is { AuthenticationSchemes.Count: > 0 })
        {
            var missing = policy
                .AuthenticationSchemes.Where(name =>
                    schemes?.GetSchemeAsync(name).GetAwaiter().GetResult() is null
                )
                .ToList();
            if (missing.Count == 0)
                return;

            throw new InvalidOperationException(
                "The dashboard's authorization policy names the authentication scheme(s) "
                    + $"{string.Join(", ", missing.Select(m => $"'{m}'"))}, which are not "
                    + "registered, so every request it refuses would fail with a 500. Register "
                    + "them with builder.Services.AddAuthentication().Add...(...)."
            );
        }

        if (schemes?.GetDefaultChallengeSchemeAsync().GetAwaiter().GetResult() is null)
            throw new InvalidOperationException(
                "The dashboard requires authorization, but the host has no authentication scheme "
                    + "to challenge a caller with, so every request it refuses would fail with a "
                    + "500 instead of a 401 or 403. Register one with "
                    + "builder.Services.AddAuthentication(...) (and set it as the default when "
                    + "there is more than one), or name it on the policy with "
                    + "AddAuthenticationSchemes(...)."
            );

        if (schemes.GetDefaultForbidSchemeAsync().GetAwaiter().GetResult() is null)
        {
            var named = services
                .GetService<IOptions<AuthenticationOptions>>()
                ?.Value.DefaultForbidScheme;
            throw new InvalidOperationException(
                $"The host's default forbid scheme '{named}' is not a registered authentication "
                    + "scheme, so every signed-in caller the dashboard refuses would fail with a "
                    + "500 instead of a 403. Register it, or remove DefaultForbidScheme from "
                    + "AddAuthentication(...) so the challenge scheme forbids."
            );
        }
    }
}
