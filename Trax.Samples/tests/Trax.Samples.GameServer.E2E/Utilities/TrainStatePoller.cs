using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.Metadata;

namespace Trax.Samples.GameServer.E2E.Utilities;

public static class TrainStatePoller
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public static async Task<Metadata> WaitForTrainState(
        IDataContext dataContext,
        long metadataId,
        TrainState expectedState,
        TimeSpan? timeout = null
    )
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (DateTime.UtcNow < deadline)
        {
            dataContext.Reset();

            var metadata = await dataContext
                .Metadatas.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == metadataId);

            if (metadata?.TrainState == expectedState)
                return metadata;

            await Task.Delay(PollInterval);
        }

        dataContext.Reset();
        var final = await dataContext
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == metadataId);

        throw new TimeoutException(
            $"Metadata {metadataId} did not reach state {expectedState} within {(timeout ?? DefaultTimeout).TotalSeconds}s. "
                + $"Current state: {final?.TrainState.ToString() ?? "not found"}, "
                + $"Failure: {final?.FailureReason ?? "none"}"
        );
    }

    public static async Task<Metadata> WaitForMetadataByTrainName(
        IDataContext dataContext,
        string trainNameContains,
        TrainState expectedState,
        TimeSpan? timeout = null,
        long? afterMetadataId = null
    )
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (DateTime.UtcNow < deadline)
        {
            dataContext.Reset();

            var query = dataContext
                .Metadatas.AsNoTracking()
                .Where(m => m.Name != null && m.Name.Contains(trainNameContains));

            if (afterMetadataId.HasValue)
                query = query.Where(m => m.Id > afterMetadataId.Value);

            var metadata = await query
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync(m => m.TrainState == expectedState);

            if (metadata != null)
                return metadata;

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"No metadata containing '{trainNameContains}' reached state {expectedState} "
                + $"within {(timeout ?? DefaultTimeout).TotalSeconds}s."
        );
    }

    public static async Task<Metadata> WaitForMetadataByManifestId(
        IDataContext dataContext,
        long manifestId,
        TrainState expectedState,
        TimeSpan? timeout = null,
        long? afterMetadataId = null
    )
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (DateTime.UtcNow < deadline)
        {
            dataContext.Reset();

            var query = dataContext.Metadatas.AsNoTracking().Where(m => m.ManifestId == manifestId);

            if (afterMetadataId.HasValue)
                query = query.Where(m => m.Id > afterMetadataId.Value);

            var metadata = await query
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync(m => m.TrainState == expectedState);

            if (metadata != null)
                return metadata;

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"No metadata for manifest {manifestId} reached state {expectedState} "
                + $"within {(timeout ?? DefaultTimeout).TotalSeconds}s."
        );
    }

    public static async Task WaitForDeadLetter(
        IDataContext dataContext,
        long manifestId,
        TimeSpan? timeout = null
    )
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (DateTime.UtcNow < deadline)
        {
            dataContext.Reset();

            var deadLetter = await dataContext
                .DeadLetters.AsNoTracking()
                .FirstOrDefaultAsync(dl => dl.ManifestId == manifestId);

            if (deadLetter != null)
                return;

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"No dead letter for manifest {manifestId} appeared "
                + $"within {(timeout ?? DefaultTimeout).TotalSeconds}s."
        );
    }

    public static async Task WaitForDeadLetterResolved(
        IDataContext dataContext,
        long deadLetterId,
        DeadLetterStatus expectedStatus,
        TimeSpan? timeout = null
    )
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (DateTime.UtcNow < deadline)
        {
            dataContext.Reset();

            var deadLetter = await dataContext
                .DeadLetters.AsNoTracking()
                .FirstOrDefaultAsync(dl => dl.Id == deadLetterId);

            if (deadLetter?.Status == expectedStatus)
                return;

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"Dead letter {deadLetterId} did not reach status {expectedStatus} "
                + $"within {(timeout ?? DefaultTimeout).TotalSeconds}s."
        );
    }

    public static async Task<Effect.Models.WorkQueue.WorkQueue> WaitForWorkQueueByManifestId(
        IDataContext dataContext,
        long manifestId,
        TimeSpan? timeout = null,
        long? afterWorkQueueId = null
    )
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (DateTime.UtcNow < deadline)
        {
            dataContext.Reset();

            var query = dataContext
                .WorkQueues.AsNoTracking()
                .Where(wq => wq.ManifestId == manifestId);

            if (afterWorkQueueId.HasValue)
                query = query.Where(wq => wq.Id > afterWorkQueueId.Value);

            var entry = await query.OrderByDescending(wq => wq.Id).FirstOrDefaultAsync();

            if (entry != null)
                return entry;

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"No work queue entry for manifest {manifestId} appeared "
                + $"within {(timeout ?? DefaultTimeout).TotalSeconds}s."
        );
    }

    public static async Task EnsureNoMetadataAppears(
        IDataContext dataContext,
        string trainNameContains,
        TimeSpan waitDuration,
        long? afterMetadataId = null
    )
    {
        var deadline = DateTime.UtcNow + waitDuration;

        while (DateTime.UtcNow < deadline)
        {
            dataContext.Reset();

            var query = dataContext
                .Metadatas.AsNoTracking()
                .Where(m => m.Name != null && m.Name.Contains(trainNameContains));

            if (afterMetadataId.HasValue)
                query = query.Where(m => m.Id > afterMetadataId.Value);

            var found = await query.AnyAsync();

            if (found)
                throw new InvalidOperationException(
                    $"Unexpected metadata containing '{trainNameContains}' appeared "
                        + $"when none was expected."
                );

            await Task.Delay(PollInterval);
        }
    }
}
