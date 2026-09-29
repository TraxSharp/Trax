using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Queries under <c>operations.config</c>. Returns the live scheduler runtime settings;
/// matches what the dashboard's ServerSettingsPage reads, since both go through
/// <see cref="IOperationsService"/>.
/// </summary>
public class ConfigQueries
{
    public SchedulerConfigSnapshot GetScheduler([Service] IOperationsService operationsService) =>
        operationsService.GetSchedulerConfig();

    /// <summary>
    /// The API host's environment name (<c>IHostEnvironment.EnvironmentName</c>), which the
    /// dashboard shows as its environment badge.
    /// </summary>
    public string GetEnvironmentName([Service] IHostEnvironment environment) =>
        environment.EnvironmentName;

    /// <summary>
    /// The API host's <c>Logging:LogLevel</c> section, <c>Default</c> first and the rest in the
    /// order configuration lists them (by key). Only that section is read; nothing else in configuration is reachable
    /// from here. Empty when the host configures none.
    /// </summary>
    public IReadOnlyList<LogLevelSetting> GetLogLevels([Service] IConfiguration configuration) =>
        configuration
            .GetSection("Logging:LogLevel")
            .GetChildren()
            .Select(section => new LogLevelSetting(section.Key, section.Value ?? "Information"))
            .OrderBy(entry => entry.Category == "Default" ? 0 : 1)
            .ThenBy(entry => entry.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
