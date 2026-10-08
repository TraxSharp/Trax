using System.Security.Cryptography;
using System.Text;
using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Faults;
using Trax.Samples.Recovery.Index;

namespace Trax.Samples.Recovery.Trains.Ingest.Junctions;

/// <summary>
/// Sums up what was written as counts and one fingerprint: a SHA-256 over each work's id, content
/// hash and resolution, in id order. The same content resolved the same way gives the same
/// fingerprint, so a caller can tell whether a partition changed without reading its works. The
/// run is over after this, so anything still armed for the partition is removed.
/// </summary>
public class FingerprintPartition(FaultInjector faults, DemoPace pace)
    : EffectJunction<UpsertedPartition, IngestPartitionResult>
{
    public override async Task<IngestPartitionResult> Run(UpsertedPartition partition)
    {
        await Task.Delay(pace.StepDelay, CancellationToken);
        faults.Disarm(IndexFixture.PartitionKey(partition.Source, partition.Month));

        var lines = partition
            .Works.OrderBy(w => w.SourceId, StringComparer.Ordinal)
            .Select(w =>
                $"{w.SourceId}\u001f{w.ContentHash}\u001f{w.Resolution}\u001f{w.ExistingWorkId}"
            );
        var fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)))
        );

        int Count(string resolution) => partition.Works.Count(w => w.Resolution == resolution);

        return new IngestPartitionResult(
            partition.Source,
            partition.Month,
            partition.Works.Count,
            Count(Resolutions.Created),
            Count(Resolutions.Merged),
            Count(Resolutions.NeedsReview),
            partition.Unsure,
            fingerprint
        );
    }
}
