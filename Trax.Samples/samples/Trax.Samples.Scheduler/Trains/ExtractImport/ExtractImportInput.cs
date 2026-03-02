using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduler.Trains.ExtractImport;

/// <summary>
/// Input for the ExtractImport train.
/// Each scheduled instance targets a specific table and index partition.
/// </summary>
public record ExtractImportInput : IManifestProperties
{
    /// <summary>
    /// The name of the table to extract/import (e.g., "Customer", "Transaction").
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// The index partition to process.
    /// </summary>
    public required int Index { get; init; }
}
