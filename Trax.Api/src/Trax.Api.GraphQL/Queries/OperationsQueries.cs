using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Predefined operational queries: health, trains, manifests, manifest groups, and execution history.
/// </summary>
public class OperationsQueries
{
    public async Task<HealthStatus> GetHealth(
        [Service] ITraxHealthService healthService,
        CancellationToken ct
    )
    {
        return await healthService.GetHealthAsync(ct);
    }

    public IReadOnlyList<TrainInfo> GetTrains([Service] ITrainDiscoveryService discoveryService)
    {
        return discoveryService
            .DiscoverTrains()
            .Select(r => new TrainInfo(
                r.ServiceTypeName,
                r.ImplementationTypeName,
                r.InputTypeName,
                r.OutputTypeName,
                r.Lifetime.ToString(),
                GetInputSchema(r.InputType),
                r.RequiredPolicies,
                r.RequiredRoles,
                r.IsQuery,
                r.IsMutation,
                r.GraphQLName,
                r.IsBroadcastEnabled
            ))
            .ToList();
    }

    public async Task<PagedResult<ManifestSummary>> GetManifests(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var query = db.Manifests.AsNoTracking().OrderByDescending(m => m.Id);
        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Skip(skip)
            .Take(take)
            .Select(m => new ManifestSummary(
                m.Id,
                m.ExternalId,
                m.Name,
                m.IsEnabled,
                m.ScheduleType,
                m.CronExpression,
                m.IntervalSeconds,
                m.MaxRetries,
                m.TimeoutSeconds,
                m.LastSuccessfulRun,
                m.ManifestGroupId,
                m.DependsOnManifestId,
                m.Priority
            ))
            .ToListAsync(ct);

        return new PagedResult<ManifestSummary>(items, totalCount, skip, take);
    }

    public async Task<ManifestSummary?> GetManifest(
        long id,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        return await db
            .Manifests.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new ManifestSummary(
                m.Id,
                m.ExternalId,
                m.Name,
                m.IsEnabled,
                m.ScheduleType,
                m.CronExpression,
                m.IntervalSeconds,
                m.MaxRetries,
                m.TimeoutSeconds,
                m.LastSuccessfulRun,
                m.ManifestGroupId,
                m.DependsOnManifestId,
                m.Priority
            ))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PagedResult<ManifestGroupSummary>> GetManifestGroups(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var query = db.ManifestGroups.AsNoTracking().OrderByDescending(g => g.Id);
        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Skip(skip)
            .Take(take)
            .Select(g => new ManifestGroupSummary(
                g.Id,
                g.Name,
                g.MaxActiveJobs,
                g.Priority,
                g.IsEnabled,
                g.CreatedAt,
                g.UpdatedAt
            ))
            .ToListAsync(ct);

        return new PagedResult<ManifestGroupSummary>(items, totalCount, skip, take);
    }

    public async Task<PagedResult<ExecutionSummary>> GetExecutions(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var query = db.Metadatas.AsNoTracking().OrderByDescending(m => m.StartTime);
        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Skip(skip)
            .Take(take)
            .Select(m => new ExecutionSummary(
                m.Id,
                m.ExternalId,
                m.Name,
                m.TrainState,
                m.StartTime,
                m.EndTime,
                m.FailureStep,
                m.FailureReason,
                m.ManifestId,
                m.CancellationRequested,
                m.HostName,
                m.HostEnvironment,
                m.HostInstanceId
            ))
            .ToListAsync(ct);

        return new PagedResult<ExecutionSummary>(items, totalCount, skip, take);
    }

    public async Task<ExecutionSummary?> GetExecution(
        long id,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        return await db
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new ExecutionSummary(
                m.Id,
                m.ExternalId,
                m.Name,
                m.TrainState,
                m.StartTime,
                m.EndTime,
                m.FailureStep,
                m.FailureReason,
                m.ManifestId,
                m.CancellationRequested,
                m.HostName,
                m.HostEnvironment,
                m.HostInstanceId
            ))
            .FirstOrDefaultAsync(ct);
    }

    private static List<InputPropertySchema> GetInputSchema(Type inputType)
    {
        return inputType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .Select(p => new InputPropertySchema(
                p.Name,
                GetFriendlyTypeName(p.PropertyType),
                Nullable.GetUnderlyingType(p.PropertyType) is not null
                    || !p.PropertyType.IsValueType
            ))
            .ToList();
    }

    private static string GetFriendlyTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return $"{GetFriendlyTypeName(underlying)}?";

        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name[..type.Name.IndexOf('`')];
        var args = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
        return $"{name}<{args}>";
    }
}
