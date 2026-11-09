using System.Reflection;
using System.Security.Claims;
using HotChocolate.Resolvers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.LogLevels;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Queries under <c>operations.config</c>. Returns the live scheduler runtime settings;
/// matches what the dashboard's ServerSettingsPage reads, since both go through
/// <see cref="IOperationsService"/>.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/queries/config">SDK reference</seealso>
public class ConfigQueries
{
    /// <summary>
    /// The scheduler's current runtime settings, as this process holds them in memory (including
    /// any change made with <c>updateScheduler</c>).
    /// </summary>
    public SchedulerConfigSnapshot GetScheduler([Service] IOperationsService operationsService) =>
        operationsService.GetSchedulerConfig();

    /// <summary>
    /// The API host's environment name (<c>IHostEnvironment.EnvironmentName</c>), which the
    /// dashboard shows as its environment badge.
    /// </summary>
    public string GetEnvironmentName([Service] IHostEnvironment environment) =>
        environment.EnvironmentName;

    /// <summary>
    /// The version of Trax serving this API, such as <c>1.46.0</c>: the Trax.Api.GraphQL
    /// package's version without its build metadata. It is what the operations console's footer
    /// shows, and it says which Trax release the host runs, not the host application's own
    /// version.
    /// </summary>
    public string GetVersion() => TraxVersion;

    /// <summary>
    /// Whether the caller may manage persisted operations on this host: the host serves the
    /// <c>persistedOperations</c> root (it calls <c>UsePersistedOperations</c> with
    /// <c>GateManagement</c>) and the caller meets that gate. False on a host without persisted
    /// operations, on one that declares no management gate, and for a caller the gate refuses. The
    /// operations console reads it to decide whether to show its persisted-operation pages, so every
    /// host answers the question without an error.
    /// </summary>
    /// <param name="user">The caller, from the request; not a GraphQL argument.</param>
    /// <param name="context">The resolver context the gate is evaluated in; not a GraphQL argument.</param>
    public async Task<bool> GetCanManagePersistedOperations(
        [GlobalState(nameof(ClaimsPrincipal))] ClaimsPrincipal? user,
        IResolverContext context
    )
    {
        if (user is null)
            return false;
        var access = context.Services.GetService<IPersistedOperationsManagementAccess>();
        return access is not null
            && await access.AllowsAsync(user, context.RequestAborted).ConfigureAwait(false);
    }

    private static readonly string TraxVersion =
        typeof(ConfigQueries)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "unknown";

    /// <summary>
    /// The level each category configured under the API host's <c>Logging:LogLevel</c> section
    /// filters at now, <c>Default</c> first and the rest by name, including any change made with
    /// <c>setLogLevels</c>. Read through <see cref="ILogLevelService"/>, the call the dashboard's
    /// server settings make. Only that section is read; nothing else in configuration is reachable
    /// from here. Empty when the host configures none.
    /// </summary>
    /// <remarks>
    /// A host that registers no <see cref="ILogLevelService"/> (one that does not call
    /// <c>AddScheduler</c>) gets the configured section as written, with no runtime changes, and
    /// <c>setLogLevels</c> refuses.
    /// </remarks>
    /// <param name="configuration">Resolved from DI when the host registers it; not a GraphQL argument.</param>
    /// <param name="logLevels">Resolved from DI when the host registers it; not a GraphQL argument.</param>
    public IReadOnlyList<LogLevelSetting> GetLogLevels(
        [Service] IConfiguration? configuration = null,
        [Service] ILogLevelService? logLevels = null
    ) =>
        logLevels is null
            ? (configuration?.GetSection("Logging:LogLevel").GetChildren() ?? [])
                .Select(section => new LogLevelSetting(section.Key, section.Value ?? "Information")
                {
                    ConfiguredLevel = section.Value,
                })
                .OrderBy(entry => entry.Category == "Default" ? 0 : 1)
                .ThenBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : logLevels
                .GetLogLevels()
                .Select(level => new LogLevelSetting(level.Category, level.Level.ToString())
                {
                    ConfiguredLevel = level.ConfiguredLevel,
                    Overridden = level.Overridden,
                })
                .ToList();
}
