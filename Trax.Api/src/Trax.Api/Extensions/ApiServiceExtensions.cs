using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trax.Api.Services.Authorization;
using Trax.Api.Services.HealthCheck;
using Trax.Api.Services.Principal;
using Trax.Mediator.Services.Principal;
using Trax.Mediator.Services.TrainAuthorization;

namespace Trax.Api.Extensions;

public static class ApiServiceExtensions
{
    /// <summary>
    /// Registers Trax API core services including health checks and per-train authorization.
    /// Core train discovery and execution services are provided by Trax.Mediator's
    /// <c>AddMediator()</c>.
    /// </summary>
    public static IServiceCollection AddTraxApi(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITraxHealthService, TraxHealthService>();
        services.AddScoped<ITrainAuthorizationService, TrainAuthorizationService>();

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
