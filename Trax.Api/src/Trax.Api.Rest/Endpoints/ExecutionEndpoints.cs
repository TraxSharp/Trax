using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Api.Rest.Endpoints;

public static class ExecutionEndpoints
{
    public static RouteGroupBuilder MapExecutionEndpoints(this RouteGroupBuilder group)
    {
        var executions = group.MapGroup("/executions").WithTags("Executions");

        executions.MapGet("/", GetExecutions).WithName("GetExecutions");
        executions.MapGet("/{id:long}", GetExecution).WithName("GetExecution");

        return group;
    }

    private static async Task<IResult> GetExecutions(
        IDataContextProviderFactory dataContextFactory,
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
                m.CancellationRequested
            ))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ExecutionSummary>(items, totalCount, skip, take));
    }

    private static async Task<IResult> GetExecution(
        long id,
        IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var execution = await db
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
                m.CancellationRequested
            ))
            .FirstOrDefaultAsync(ct);

        return execution is not null ? Results.Ok(execution) : Results.NotFound();
    }
}
