using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trax.Api.Services.Authorization;
using Trax.Api.Services.HealthCheck;
using Trax.Api.Services.Metrics;
using Trax.Api.Services.Principal;
using Trax.Mediator.Services.Principal;
using Trax.Mediator.Services.TrainAuthorization;

namespace Trax.Api.Extensions;

/// <summary>
/// Registers the services behind the Trax GraphQL API. <c>AddTraxGraphQL</c> calls
/// <see cref="AddTraxApi"/> for you.
/// </summary>
public static class ApiServiceExtensions
{
    /// <summary>
    /// Registers Trax API core services including health checks and per-train authorization.
    /// Core train discovery and execution services are provided by Trax.Mediator's
    /// <c>AddMediator()</c>.
    /// </summary>
    /// <remarks>
    /// Call it after <c>services.AddTrax(trax => ...)</c> with <c>AddMediator(...)</c> inside: it
    /// replaces the mediator's principal provider with one that reads the current HTTP request, and
    /// called first, the mediator's registration would win. <c>AddTraxGraphQL()</c> calls this for
    /// you, and its endpoint is mapped by <c>UseTraxGraphQL()</c>; call it directly only on a host
    /// that uses these services without the GraphQL schema. There is no <c>Use*</c> counterpart.
    /// </remarks>
    public static IServiceCollection AddTraxApi(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITraxHealthService, TraxHealthService>();
        services.AddScoped<ITrainAuthorizationService, TrainAuthorizationService>();

        // Singleton so the CPU sampler's previous-sample delta survives across requests.
        services.TryAddSingleton<ProcessCpuSampler>();

        // Replace the mediator's null-default principal provider with one backed
        // by IHttpContextAccessor so per-principal concurrency caps work for
        // real HTTP requests. Use Replace (not Add) because the mediator already
        // registered the null provider and both would otherwise resolve.
        services.Replace(
            ServiceDescriptor.Singleton<ICurrentPrincipalProvider, HttpContextPrincipalProvider>()
        );

        return services;
    }
}
