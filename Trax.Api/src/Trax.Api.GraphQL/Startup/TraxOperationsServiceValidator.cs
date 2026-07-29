using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Fails fast at host startup when the operations (admin) surface is exposed via
/// <c>ExposeOperationQueries()</c> / <c>ExposeOperationMutations()</c> but the services those
/// resolvers depend on are not registered. Without this, the schema builds fine and the operations
/// only fail at request time with a masked "Unexpected Execution Error".
/// </summary>
/// <remarks>
/// Checks <see cref="IServiceProviderIsService"/> (registration, not resolution), so it is
/// independent of the order services were added relative to <c>AddTraxGraphQL()</c>.
/// </remarks>
internal sealed class TraxOperationsServiceValidator(
    IServiceProviderIsService isService,
    bool mutationsExposed
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!isService.IsService(typeof(IOperationsService)))
            throw new InvalidOperationException(
                "AddTraxGraphQL() exposes the operations surface (ExposeOperationQueries / "
                    + "ExposeOperationMutations), but IOperationsService is not registered, so those "
                    + "operations would throw at request time. Register the backing services before the "
                    + "host starts:\n"
                    + "  - call AddScheduler(...) (registers IOperationsService + ITraxScheduler), or\n"
                    + "  - for an API-only host, call AddTraxJobRunner() and\n"
                    + "    services.AddScoped<IOperationsService, OperationsService>();"
            );

        if (mutationsExposed && !isService.IsService(typeof(ITraxScheduler)))
            throw new InvalidOperationException(
                "AddTraxGraphQL() exposes the operations mutations (ExposeOperationMutations), but "
                    + "ITraxScheduler is not registered, so trigger/enable/disable/cancel and dead-letter "
                    + "mutations would throw at request time. Call AddScheduler(...) or AddTraxJobRunner() "
                    + "before the host starts."
            );

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
