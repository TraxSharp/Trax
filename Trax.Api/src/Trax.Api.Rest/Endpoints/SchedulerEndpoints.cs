using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Rest.Endpoints;

public static class SchedulerEndpoints
{
    public static RouteGroupBuilder MapSchedulerEndpoints(this RouteGroupBuilder group)
    {
        var scheduler = group.MapGroup("/scheduler").WithTags("Scheduler");

        scheduler.MapPost("/trigger/{externalId}", TriggerManifest).WithName("TriggerManifest");
        scheduler
            .MapPost("/trigger/{externalId}/delayed", TriggerManifestDelayed)
            .WithName("TriggerManifestDelayed");
        scheduler.MapPost("/schedule-once", ScheduleOnce).WithName("ScheduleOnce");
        scheduler.MapPost("/disable/{externalId}", DisableManifest).WithName("DisableManifest");
        scheduler.MapPost("/enable/{externalId}", EnableManifest).WithName("EnableManifest");
        scheduler.MapPost("/cancel/{externalId}", CancelManifest).WithName("CancelManifest");
        scheduler.MapPost("/groups/{groupId:long}/trigger", TriggerGroup).WithName("TriggerGroup");
        scheduler.MapPost("/groups/{groupId:long}/cancel", CancelGroup).WithName("CancelGroup");

        return group;
    }

    private static async Task<IResult> TriggerManifest(
        string externalId,
        ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.TriggerAsync(externalId, ct);
        return Results.Ok(new OperationResponse(true, Message: "Manifest triggered"));
    }

    private static async Task<IResult> TriggerManifestDelayed(
        string externalId,
        TriggerDelayedRequest request,
        ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.TriggerAsync(externalId, request.Delay, ct);
        return Results.Ok(
            new OperationResponse(true, Message: $"Manifest triggered with {request.Delay} delay")
        );
    }

    private static Task<IResult> ScheduleOnce(
        ScheduleOnceRequest request,
        ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        // ScheduleOnce requires generic type parameters, which we can't resolve
        // from a string train name at the ITraxScheduler level. For now, this
        // endpoint queues the train with a delay via the execution service.
        // A future version may add an untyped ScheduleOnce to ITraxScheduler.
        return Task.FromResult<IResult>(
            Results.StatusCode(501) // Not Implemented
        );
    }

    private static async Task<IResult> DisableManifest(
        string externalId,
        ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.DisableAsync(externalId, ct);
        return Results.Ok(new OperationResponse(true, Message: "Manifest disabled"));
    }

    private static async Task<IResult> EnableManifest(
        string externalId,
        ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.EnableAsync(externalId, ct);
        return Results.Ok(new OperationResponse(true, Message: "Manifest enabled"));
    }

    private static async Task<IResult> CancelManifest(
        string externalId,
        ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        var count = await scheduler.CancelAsync(externalId, ct);
        return Results.Ok(
            new OperationResponse(true, Count: count, Message: "Cancellation requested")
        );
    }

    private static async Task<IResult> TriggerGroup(
        long groupId,
        ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        var count = await scheduler.TriggerGroupAsync(groupId, ct);
        return Results.Ok(
            new OperationResponse(true, Count: count, Message: $"{count} manifest(s) triggered")
        );
    }

    private static async Task<IResult> CancelGroup(
        long groupId,
        ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        var count = await scheduler.CancelGroupAsync(groupId, ct);
        return Results.Ok(
            new OperationResponse(
                true,
                Count: count,
                Message: $"Cancellation requested for {count} execution(s)"
            )
        );
    }
}
