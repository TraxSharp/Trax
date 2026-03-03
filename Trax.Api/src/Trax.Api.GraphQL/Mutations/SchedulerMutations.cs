using HotChocolate.Types;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.GraphQL.Mutations;

[ExtendObjectType(OperationTypeNames.Mutation)]
public class SchedulerMutations
{
    public async Task<OperationResponse> TriggerManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.TriggerAsync(externalId, ct);
        return new OperationResponse(true, Message: "Manifest triggered");
    }

    public async Task<OperationResponse> TriggerManifestDelayed(
        string externalId,
        TimeSpan delay,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.TriggerAsync(externalId, delay, ct);
        return new OperationResponse(true, Message: $"Manifest triggered with {delay} delay");
    }

    public async Task<OperationResponse> DisableManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.DisableAsync(externalId, ct);
        return new OperationResponse(true, Message: "Manifest disabled");
    }

    public async Task<OperationResponse> EnableManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        await scheduler.EnableAsync(externalId, ct);
        return new OperationResponse(true, Message: "Manifest enabled");
    }

    public async Task<OperationResponse> CancelManifest(
        string externalId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        var count = await scheduler.CancelAsync(externalId, ct);
        return new OperationResponse(true, Count: count, Message: "Cancellation requested");
    }

    public async Task<OperationResponse> TriggerGroup(
        long groupId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        var count = await scheduler.TriggerGroupAsync(groupId, ct);
        return new OperationResponse(true, Count: count, Message: $"{count} manifest(s) triggered");
    }

    public async Task<OperationResponse> CancelGroup(
        long groupId,
        [Service] ITraxScheduler scheduler,
        CancellationToken ct
    )
    {
        var count = await scheduler.CancelGroupAsync(groupId, ct);
        return new OperationResponse(
            true,
            Count: count,
            Message: $"Cancellation requested for {count} execution(s)"
        );
    }
}
