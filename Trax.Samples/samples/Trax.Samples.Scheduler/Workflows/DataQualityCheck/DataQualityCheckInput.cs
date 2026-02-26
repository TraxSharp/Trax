using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduler.Workflows.DataQualityCheck;

/// <summary>
/// Input for the DataQualityCheck workflow.
/// Populated at runtime by the parent ExtractImport workflow when anomalies are detected.
/// </summary>
public record DataQualityCheckInput : IManifestProperties
{
    /// <summary>
    /// The name of the table to check (e.g., "Customer", "Transaction").
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// The index partition where anomalies were detected.
    /// </summary>
    public required int Index { get; init; }

    /// <summary>
    /// The number of anomalous rows detected by the parent extraction.
    /// </summary>
    public required int AnomalyCount { get; init; }
}
