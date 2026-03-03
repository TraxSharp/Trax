using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Api.Rest.Endpoints;

public static class ManifestGroupEndpoints
{
    public static RouteGroupBuilder MapManifestGroupEndpoints(this RouteGroupBuilder group)
    {
        var groups = group.MapGroup("/manifest-groups").WithTags("ManifestGroups");

        groups.MapGet("/", GetManifestGroups).WithName("GetManifestGroups");
        groups.MapGet("/{id:long}", GetManifestGroup).WithName("GetManifestGroup");

        return group;
    }

    private static async Task<IResult> GetManifestGroups(
        IDataContextProviderFactory dataContextFactory,
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

        return Results.Ok(new PagedResult<ManifestGroupSummary>(items, totalCount, skip, take));
    }

    private static async Task<IResult> GetManifestGroup(
        long id,
        IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var group = await db
            .ManifestGroups.AsNoTracking()
            .Where(g => g.Id == id)
            .Select(g => new ManifestGroupSummary(
                g.Id,
                g.Name,
                g.MaxActiveJobs,
                g.Priority,
                g.IsEnabled,
                g.CreatedAt,
                g.UpdatedAt
            ))
            .FirstOrDefaultAsync(ct);

        return group is not null ? Results.Ok(group) : Results.NotFound();
    }
}
