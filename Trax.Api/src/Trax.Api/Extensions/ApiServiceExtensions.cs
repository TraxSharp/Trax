using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Services.Authorization;
using Trax.Api.Services.HealthCheck;
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
        return services;
    }
}
