using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;

namespace Trax.Samples.EnergyHub.E2E.Utilities;

public static class TrainStatePoller
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

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
}
