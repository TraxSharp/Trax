using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radzen;
using Trax.Dashboard.Components;
using Trax.Dashboard.Configuration;
using Trax.Dashboard.Services.Authorization;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Services.ThemeState;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Extensions;

/// <summary>
/// Registration and mounting for the Trax Dashboard: <c>builder.AddTraxDashboard()</c> after
/// <c>AddTrax(...)</c>, then <c>app.UseTraxDashboard()</c>.
/// </summary>
public static class DashboardServiceExtensions
{
    /// <summary>
    /// Registers the Trax Dashboard on the host: its options, per-session UI services, Radzen
    /// components and Blazor Server interactive components. This is the recommended overload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call it after <c>builder.Services.AddTrax(...)</c>, once. It checks for the Trax registration
    /// and throws <see cref="InvalidOperationException"/> when <c>AddTrax</c> has not run yet or
    /// when <c>AddTraxDashboard</c> already has.
    /// </para>
    /// <para>
    /// Unlike the <see cref="IServiceCollection"/> overload, this one also changes the host outside
    /// DI. Outside the Development environment it calls <c>builder.WebHost.UseStaticWebAssets()</c>,
    /// which ASP.NET Core only does automatically in Development, so the dashboard's and Radzen's
    /// CSS and JS under <c>_content/</c> are served in every environment.
    /// </para>
    /// <para>
    /// Choose an authorization posture in <paramref name="configure"/>:
    /// <see cref="DashboardOptions.RequirePolicy"/>, <see cref="DashboardOptions.RequireRoles"/> or
    /// <see cref="DashboardOptions.AllowAnonymousDashboard"/>. <see cref="UseTraxDashboard"/>
    /// applies it to every dashboard endpoint and refuses to start without one.
    /// </para>
    /// </remarks>
    /// <param name="builder">The host builder, after <c>AddTrax(...)</c> has been called on its services.</param>
    /// <param name="configure">Optional callback to adjust <see cref="DashboardOptions"/>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// <c>AddTrax(...)</c> has not been called, or <c>AddTraxDashboard</c> has already been called.
    /// </exception>
    public static WebApplicationBuilder AddTraxDashboard(
        this WebApplicationBuilder builder,
        Action<DashboardOptions>? configure = null
    )
    {
        // UseStaticWebAssets is only called automatically in Development.
        // The dashboard requires it in all environments to serve Radzen CSS/JS and
        // dashboard assets from NuGet packages via _content/ paths.
        // This is idempotent and no-ops when the manifest is absent (e.g. published apps).
        if (!builder.Environment.IsDevelopment())
            builder.WebHost.UseStaticWebAssets();

        builder.Services.AddTraxDashboard(configure);
        return builder;
    }

    /// <summary>
    /// Registers the Trax Dashboard services: its options, per-session UI services, Radzen
    /// components and Blazor Server interactive components.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call it after <c>services.AddTrax(...)</c>. It checks for the Trax registration and throws
    /// <see cref="InvalidOperationException"/> when <c>AddTrax</c> has not run yet. Call it once:
    /// a second call throws rather than replacing the first call's options.
    /// </para>
    /// <para>
    /// This overload only touches DI. Outside Development the dashboard's static assets are not
    /// served unless you call <c>builder.WebHost.UseStaticWebAssets()</c> yourself before
    /// <c>builder.Build()</c>. Prefer the
    /// <see cref="AddTraxDashboard(WebApplicationBuilder, Action{DashboardOptions}?)"/> overload,
    /// which does that for you.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection, after <c>AddTrax(...)</c> has been called on it.</param>
    /// <param name="configure">Optional callback to adjust <see cref="DashboardOptions"/>.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// <c>AddTrax(...)</c> has not been called, or <c>AddTraxDashboard</c> has already been called.
    /// </exception>
    public static IServiceCollection AddTraxDashboard(
        this IServiceCollection services,
        Action<DashboardOptions>? configure = null
    )
    {
        if (!services.Any(sd => sd.ServiceType == typeof(TraxMarker)))
            throw new InvalidOperationException(
                "AddTraxDashboard() requires AddTrax() to be called first. "
                    + "Call services.AddTrax(trax => ...) before services.AddTraxDashboard()."
            );

        // A second call would register a second options singleton, and the last one would win:
        // a shared bootstrap calling AddTraxDashboard(o => o.AllowAnonymousDashboard()) after the
        // host's AddTraxDashboard(o => o.RequireRoles("Admin")) would map the dashboard ungated.
        if (services.Any(sd => sd.ServiceType == typeof(DashboardOptions)))
            throw new InvalidOperationException(
                "AddTraxDashboard() has already been called on this host. Call it once, with "
                    + "every dashboard option in that one call: a second call would replace the "
                    + "first one's authorization posture."
            );

        var options = new DashboardOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        // Re-checks the posture inside an established circuit: on an interval, whenever the
        // host's AuthenticationStateProvider reports a change, and before every inbound circuit
        // message. It reads the host's provider and registers none of its own.
        services.AddScoped<DashboardCircuitAuthorization>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<CircuitHandler, DashboardAuthorizationCircuitHandler>()
        );

        services.AddScoped<ILocalStorageService, LocalStorageService>();
        services.AddScoped<IThemeStateService, ThemeStateService>();
        services.AddScoped<IDashboardSettingsService, DashboardSettingsService>();

        services.AddRadzenComponents();

        services.AddRazorComponents().AddInteractiveServerComponents();

        return services;
    }

    /// <summary>
    /// Mounts the Trax Dashboard into the application's request pipeline at <c>/trax</c>, and
    /// applies the authorization posture chosen in <c>AddTraxDashboard</c> to every endpoint it
    /// maps: the pages and the Blazor circuit hub.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requires <c>AddTraxDashboard</c> to have been called during registration. It adds
    /// <c>UseStaticFiles()</c>, <c>UseAntiforgery()</c>, <c>MapStaticAssets()</c> and maps the
    /// dashboard's Razor components with interactive server render mode, so it changes the host's
    /// middleware pipeline.
    /// </para>
    /// <para>
    /// Every dashboard page carries a compile-time <c>@page "/trax/..."</c> route. A non-default
    /// <paramref name="routePrefix"/> only changes the links the sidebar builds; the pages stay
    /// under <c>/trax</c>, so leave it at its default.
    /// </para>
    /// <para>
    /// The dashboard's pages queue, run and inspect work through the Scheduler, so the host must
    /// call <c>AddScheduler()</c> inside <c>AddTrax(...)</c>. Without it this method throws rather
    /// than mapping pages that fail on their first request.
    /// </para>
    /// <para>
    /// Inside an established circuit the posture is re-checked against the host's
    /// <c>AuthenticationStateProvider</c>: whenever it reports a change, on an interval, and
    /// before every message the circuit receives. A circuit whose user no longer satisfies it is
    /// closed. The dashboard registers no <c>AuthenticationStateProvider</c> of its own.
    /// </para>
    /// <para>
    /// The host project must set <c>&lt;RequiresAspNetWebAssets&gt;true&lt;/RequiresAspNetWebAssets&gt;</c>
    /// in its csproj. Without it <c>_framework/blazor.web.js</c> is missing, the Blazor Server
    /// circuit never connects, and the dashboard renders but does not respond to clicks.
    /// </para>
    /// </remarks>
    /// <param name="app">The built application.</param>
    /// <param name="routePrefix">The prefix the sidebar links use. Defaults to <c>/trax</c>, where the pages are.</param>
    /// <param name="title">Optional title for the dashboard header; overrides <see cref="DashboardOptions.Title"/>.</param>
    /// <returns>
    /// The convention builder for those endpoints, so the host can add its own conventions
    /// (<c>RequireHost</c>, rate limiting, a further <c>RequireAuthorization</c>). Conventions
    /// added there are additive; they do not replace the posture.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <c>AddTraxDashboard</c> was not called; no posture was chosen (call
    /// <see cref="DashboardOptions.RequirePolicy"/>, <see cref="DashboardOptions.RequireRoles"/> or
    /// <see cref="DashboardOptions.AllowAnonymousDashboard"/>); the named policy is not
    /// registered; a policy or roles posture has no authentication scheme to challenge a caller
    /// with (none is the default, and the policy names none that is registered); or the Scheduler
    /// is not registered (call <c>AddScheduler()</c> inside <c>AddTrax(...)</c>).
    /// </exception>
    public static RazorComponentsEndpointConventionBuilder UseTraxDashboard(
        this WebApplication app,
        string routePrefix = "/trax",
        string? title = null
    )
    {
        routePrefix = "/" + routePrefix.Trim('/');

        var options =
            app.Services.GetService<DashboardOptions>()
            ?? throw new InvalidOperationException(
                "UseTraxDashboard() requires AddTraxDashboard() to be called first. Call "
                    + "builder.AddTraxDashboard(o => ...) after AddTrax(...) and before "
                    + "builder.Build()."
            );
        VerifyAuthorizationPosture(app, options);
        VerifySchedulerRegistered(app);

        options.RoutePrefix = routePrefix;

        if (title is not null)
            options.Title = title;
        options.EnvironmentName = app.Environment.EnvironmentName;

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapStaticAssets();
        var endpoints = app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        if (options.AnonymousAllowed)
        {
            app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Trax.Dashboard")
                .LogWarning(
                    "AllowAnonymousDashboard() is set: the Trax dashboard at {RoutePrefix} "
                        + "applies no authorization of its own, so anyone who can reach it can "
                        + "use every page, including queueing, running and cancelling trains and "
                        + "changing scheduler settings. Use RequirePolicy() or RequireRoles() in "
                        + "AddTraxDashboard() unless something in front of it is the gate.",
                    routePrefix
                );
            return endpoints;
        }

        endpoints.RequireAuthorization(
            new AuthorizeAttribute
            {
                Policy = options.Policy,
                Roles = options.Roles is null ? null : string.Join(",", options.Roles),
            }
        );
        return endpoints;
    }

    // The pages inject IOperationsService unconditionally, and only AddScheduler() registers it,
    // so without it the first request is a 500 or a dead circuit. This asks the built provider
    // whether the service is registered, without constructing it.
    private static void VerifySchedulerRegistered(WebApplication app)
    {
        var isService = app.Services.GetService<IServiceProviderIsService>();
        if (isService is not null && isService.IsService(typeof(IOperationsService)))
            return;

        throw new InvalidOperationException(
            "UseTraxDashboard() requires the Trax Scheduler: the dashboard queues, runs and "
                + "cancels trains through IOperationsService, which AddScheduler() registers. "
                + "Call AddScheduler(...) inside AddTrax(trax => ...) before mapping the dashboard."
        );
    }

    private static void VerifyAuthorizationPosture(WebApplication app, DashboardOptions options)
    {
        if (!options.HasAuthorizationPosture)
            throw new InvalidOperationException(
                "UseTraxDashboard() needs to know who may use the dashboard, which can queue, "
                    + "run and cancel trains and change scheduler settings. Choose one in "
                    + "AddTraxDashboard(o => ...): o.RequirePolicy(\"<policy>\"), "
                    + "o.RequireRoles(\"<role>\"), or o.AllowAnonymousDashboard() when something "
                    + "in front of it (a fallback policy, an ingress rule) is the gate."
            );

        if (options.AnonymousAllowed)
            return;

        // AddRazorComponents(), which AddTraxDashboard() calls, registers the authorization
        // services, so only the policy name can be missing here.
        var policy = options.Policy is null
            ? null
            : app
                .Services.GetRequiredService<IAuthorizationPolicyProvider>()
                .GetPolicyAsync(options.Policy)
                .GetAwaiter()
                .GetResult();
        if (options.Policy is not null && policy is null)
            throw new InvalidOperationException(
                $"The dashboard requires the authorization policy '{options.Policy}', which is "
                    + "not registered. Add it with builder.Services.AddAuthorization(o => "
                    + $"o.AddPolicy(\"{options.Policy}\", ...))."
            );

        VerifyChallengeScheme(app, policy);
    }

    // A refused request is answered by challenging (or forbidding) through an authentication
    // scheme: the ones the policy names, or else the host's default. With none, ASP.NET Core
    // throws on every request the posture refuses, so the dashboard answers 500 instead of 401
    // or 403. Refuse at startup instead, naming what is missing.
    private static void VerifyChallengeScheme(WebApplication app, AuthorizationPolicy? policy)
    {
        var schemes = app.Services.GetService<IAuthenticationSchemeProvider>();
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
                $"The dashboard's authorization policy names the authentication scheme(s) "
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

        // A signed-in caller the posture refuses is forbidden through the default forbid scheme,
        // which falls back to the challenge scheme unless the host names one. A name that is not
        // registered resolves to nothing, and every such refusal would fail with a 500, not 403.
        if (schemes.GetDefaultForbidSchemeAsync().GetAwaiter().GetResult() is null)
        {
            var named = app
                .Services.GetService<IOptions<AuthenticationOptions>>()
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
