using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Api.Rest.Endpoints;

public static class ManifestEndpoints
{
    public static RouteGroupBuilder MapManifestEndpoints(this RouteGroupBuilder group)
    {
        var manifests = group.MapGroup("/manifests").WithTags("Manifests");

        manifests.MapGet("/", GetManifests).WithName("GetManifests");
        manifests.MapGet("/{id:long}", GetManifest).WithName("GetManifest");

        return group;
    }

    private static async Task<IResult> GetManifests(
        IDataContextProviderFactory dataContextFactory,
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

        return Results.Ok(new PagedResult<ManifestSummary>(items, totalCount, skip, take));
    }

    private static async Task<IResult> GetManifest(
        long id,
        IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var manifest = await db
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

        return manifest is not null ? Results.Ok(manifest) : Results.NotFound();
    }
}
