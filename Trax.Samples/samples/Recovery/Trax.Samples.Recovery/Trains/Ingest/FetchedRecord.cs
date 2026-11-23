using Trax.Core.Decisions;
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Ingest;

/// <summary>One record's id and its JSON, in the index's own shape.</summary>
public sealed record FetchedRecord(string SourceId, string Payload);
