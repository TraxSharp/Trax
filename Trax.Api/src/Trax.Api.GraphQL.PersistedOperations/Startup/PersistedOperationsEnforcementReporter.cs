using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.PersistedOperations.Configuration;

namespace Trax.Api.GraphQL.PersistedOperations.Startup;

/// <summary>
/// States at startup whether persisted-operation enforcement is active, so which boundary is
/// actually in front of the endpoint is a matter of record rather than of assumption.
/// </summary>
/// <remarks>
/// Enforcement is commonly keyed on the environment, which means every non-production environment
/// runs without it, including one holding production data that happens to run as Development. The
/// endpoint looks the same either way and the schema does not change, so nothing in a running
/// system reveals the answer. Saying it once at startup is what makes "persisted ops protect us"
/// checkable where it is claimed.
/// <para>
/// Logged at warning when enforcement is off, because that is the state whose consequences are
/// easy to be wrong about. Enforcement being on is ordinary and logged at information.
/// </para>
/// </remarks>
internal sealed class PersistedOperationsEnforcementReporter(
    PersistedOperationsOptions options,
    ILoggerFactory loggerFactory
) : IHostedService
{
    internal const string LoggerCategory = "Trax.Api.GraphQL.PersistedOperations";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);

        if (options.RequirePersisted)
        {
            logger.LogInformation(
                "Persisted-operation enforcement is ON. An inline query is refused unless it is "
                    + "allowlisted, introspection, or selects only the persisted-operations "
                    + "management surface. Enforcement shapes requests and is not an authorization "
                    + "boundary: per-type [TraxAuthorize] still decides who may run what."
            );

            return Task.CompletedTask;
        }

        logger.LogWarning(
            "Persisted-operation enforcement is OFF{ShadowSuffix}. Every inline query this endpoint "
                + "serves will execute, so per-type [TraxAuthorize] is the only boundary in front "
                + "of it. If enforcement is keyed on the environment, an environment holding "
                + "production data but running as Development is running without it.",
            options.LogNonPersistedRequests ? ", with shadow logging on" : string.Empty
        );

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
