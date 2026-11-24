using System.Security.Claims;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.LogLevels;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Mutations under <c>operations.config</c>. Patches mutable scheduler runtime
/// settings; the same service powers the dashboard's ServerSettingsPage save action.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/config">SDK reference</seealso>
public class ConfigMutations
{
    /// <summary>
    /// Changes the scheduler's runtime settings. Only the fields you set change; the rest keep their
    /// current values. The change takes effect at once in this process and is saved so it
    /// survives a restart. On success <c>count</c> is the number of fields that changed. A value
    /// outside the range the scheduler can run with refuses the whole patch: the result reports
    /// failure, names each offending field, and nothing is applied.
    /// </summary>
    public async Task<OperationResponse> UpdateScheduler(
        UpdateSchedulerConfigInput input,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    )
    {
        var result = await operationsService
            .UpdateSchedulerConfigAsync(input, ct)
            .ConfigureAwait(false);
        return new OperationResponse(result.Success, result.Count, result.Message);
    }

    /// <summary>
    /// Drains every host sharing the database: no host dispatches, queues a scheduled run or claims
    /// a job until the drain lapses after <paramref name="minutes"/> or <c>resume</c> ends it, while
    /// running work carries on. A schedule due meanwhile is a misfire when the drain ends. Replaces a
    /// drain in force and records the caller as who set it. The console's Drain calls the same
    /// <see cref="IOperationsService.DrainAsync"/>. A duration outside 1 to 1440 minutes is refused.
    /// </summary>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="user">The caller, recorded on the drain; not a GraphQL argument.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="minutes">How long the drain lasts; 20 when omitted.</param>
    public async Task<OperationResponse> Drain(
        [Service] IOperationsService operationsService,
        [GlobalState(nameof(ClaimsPrincipal))] ClaimsPrincipal? user,
        CancellationToken ct,
        int? minutes = null
    )
    {
        var result = await operationsService
            .DrainAsync(
                minutes is { } m ? TimeSpan.FromMinutes(m) : null,
                OperationsMutations.ActorOf(user).Display,
                ct
            )
            .ConfigureAwait(false);
        return new OperationResponse(result.Success, result.Count, result.Message);
    }

    /// <summary>
    /// Ends the drain, so every host takes work again, whoever set it. <c>count</c> is 1 when a
    /// drain was in force and 0 when none was. The console's Resume calls the same
    /// <see cref="IOperationsService.EndDrainAsync"/>.
    /// </summary>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<OperationResponse> Resume(
        [Service] IOperationsService operationsService,
        CancellationToken ct
    )
    {
        var result = await operationsService.EndDrainAsync(ct).ConfigureAwait(false);
        return new OperationResponse(result.Success, result.Count, result.Message);
    }

    /// <summary>
    /// Sets the level each listed log category filters at in THIS process, through
    /// <see cref="ILogLevelService"/>, the call the dashboard's server settings make. Only a
    /// category configured under <c>Logging:LogLevel</c> (or changed here before) can be set; a
    /// list naming any other, an empty list, or more than 1000 entries is refused whole and
    /// nothing changes. When a category is listed twice, the last level wins. The change lasts
    /// until the process restarts and does not reach the other processes of the deployment.
    /// <c>notApplied</c> lists categories whose loggers still filter at another level, which
    /// happens when the host sets the filter itself after Trax.
    /// </summary>
    /// <param name="levels">The categories to set, and their levels.</param>
    /// <param name="logLevels">Resolved from DI when the host registers it; not a GraphQL argument.</param>
    public SetLogLevelsResponse SetLogLevels(
        IReadOnlyList<LogLevelSettingInput> levels,
        [Service] ILogLevelService? logLevels = null
    )
    {
        if (logLevels is null)
            return new SetLogLevelsResponse(
                Success: false,
                0,
                [],
                "This host registers no log level service, so log levels cannot be changed at "
                    + "runtime. AddScheduler(...) registers one."
            );

        if (levels.Count > OperationsService.MaxBatchSize)
            return new SetLogLevelsResponse(
                Success: false,
                0,
                [],
                $"At most {OperationsService.MaxBatchSize} log levels can be set at once."
            );

        var result = logLevels.SetLogLevels(
            levels.Select(l => new LogLevelChange(l.Category, l.Level)).ToList()
        );
        return new SetLogLevelsResponse(
            result.Success,
            result.Count,
            result.NotApplied,
            result.Message
        );
    }
}
