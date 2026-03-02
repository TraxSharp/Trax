using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduler.Trains.TransformLoad;

/// <summary>
/// Input for the TransformLoad train.
/// Each scheduled instance targets a specific table and index partition,
/// mirroring the ExtractImport train it depends on.
/// </summary>
public record TransformLoadInput : IManifestProperties
{
    /// <summary>
    /// The name of the table to transform and load (e.g., "Customer", "Transaction").
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// The index partition to process.
    /// </summary>
    public required int Index { get; init; }
}
