using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Radzen;
using Trax.Dashboard.Components;
using Trax.Dashboard.Configuration;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Services.ThemeState;
using Trax.Effect.Configuration.TraxBuilder;

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
    /// Call it after <c>builder.Services.AddTrax(...)</c>. It checks for the Trax registration and
    /// throws <see cref="InvalidOperationException"/> when <c>AddTrax</c> has not run yet.
    /// </para>
    /// <para>
    /// Unlike the <see cref="IServiceCollection"/> overload, this one also changes the host outside
    /// DI. Outside the Development environment it calls <c>builder.WebHost.UseStaticWebAssets()</c>,
    /// which ASP.NET Core only does automatically in Development, so the dashboard's and Radzen's
    /// CSS and JS under <c>_content/</c> are served in every environment. It also appends an
    /// in-memory configuration source as the highest-priority source, so runtime overrides made
    /// from the dashboard (log levels, for example) survive a configuration reload.
    /// </para>
    /// <para>
    /// The dashboard applies no authorization of its own. Pair this with
    /// <see cref="UseTraxDashboard"/>, and gate <c>/trax</c> with your host's fallback
    /// authorization policy or path-scoped middleware before exposing it.
    /// </para>
    /// </remarks>
    /// <param name="builder">The host builder, after <c>AddTrax(...)</c> has been called on its services.</param>
    /// <param name="configure">Optional callback to adjust <see cref="DashboardOptions"/>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException"><c>AddTrax(...)</c> has not been called.</exception>
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

        // Add a MemoryConfigurationSource as the last (highest priority) source so that
        // runtime configuration overrides (e.g. log level changes from the dashboard)
        // survive IConfigurationRoot.Reload() — the memory provider's Load() is a no-op.
        builder.Configuration.AddInMemoryCollection();

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
    /// <see cref="InvalidOperationException"/> when <c>AddTrax</c> has not run yet.
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
    /// <exception cref="InvalidOperationException"><c>AddTrax(...)</c> has not been called.</exception>
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

        var options = new DashboardOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        services.AddScoped<ILocalStorageService, LocalStorageService>();
        services.AddScoped<IThemeStateService, ThemeStateService>();
        services.AddScoped<IDashboardSettingsService, DashboardSettingsService>();

        services.AddRadzenComponents();

        services.AddRazorComponents().AddInteractiveServerComponents();

        return services;
    }

    /// <summary>
    /// Mounts the Trax Dashboard into the application's request pipeline at <c>/trax</c>.
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
    /// The host project must set <c>&lt;RequiresAspNetWebAssets&gt;true&lt;/RequiresAspNetWebAssets&gt;</c>
    /// in its csproj. Without it <c>_framework/blazor.web.js</c> is missing, the Blazor Server
    /// circuit never connects, and the dashboard renders but does not respond to clicks.
    /// </para>
    /// </remarks>
    /// <param name="app">The built application.</param>
    /// <param name="routePrefix">The prefix the sidebar links use. Defaults to <c>/trax</c>, where the pages are.</param>
    /// <param name="title">Optional title for the dashboard header; overrides <see cref="DashboardOptions.Title"/>.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication UseTraxDashboard(
        this WebApplication app,
        string routePrefix = "/trax",
        string? title = null
    )
    {
        routePrefix = "/" + routePrefix.Trim('/');

        var options = app.Services.GetRequiredService<DashboardOptions>();
        options.RoutePrefix = routePrefix;

        if (title is not null)
            options.Title = title;
        options.EnvironmentName = app.Environment.EnvironmentName;

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        return app;
    }
}
