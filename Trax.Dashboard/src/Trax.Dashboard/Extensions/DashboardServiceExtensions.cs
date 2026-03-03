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

namespace Trax.Dashboard.Extensions;

public static class DashboardServiceExtensions
{
    /// <summary>
    /// Registers Trax.Core Dashboard services including Radzen components and train discovery.
    /// Also ensures static web assets (CSS, JS) from NuGet packages are available in all environments.
    /// This is the recommended overload for dashboard consumers.
    /// </summary>
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
    /// Registers Trax.Core Dashboard services including Radzen components and train discovery.
    /// When using this overload, ensure static web assets are configured for non-Development environments
    /// by calling <c>builder.WebHost.UseStaticWebAssets()</c> before <c>builder.Build()</c>.
    /// Prefer the <see cref="AddTraxDashboard(WebApplicationBuilder, Action{DashboardOptions}?)"/> overload instead.
    /// </summary>
    public static IServiceCollection AddTraxDashboard(
        this IServiceCollection services,
        Action<DashboardOptions>? configure = null
    )
    {
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
    /// Maps the Trax.Core Dashboard Blazor components at the configured route prefix.
    /// </summary>
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
