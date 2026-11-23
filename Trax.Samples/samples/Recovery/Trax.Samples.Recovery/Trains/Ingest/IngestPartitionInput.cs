using Trax.Core.Decisions;
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Ingest;

/// <summary>
/// One partition to ingest: a source and a month, as <c>yyyy-MM</c>. Nothing else, so the same
/// partition always has the same input.
/// </summary>
public sealed record IngestPartitionInput(string Source, string Month) : IManifestProperties;
