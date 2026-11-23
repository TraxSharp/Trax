using Trax.Core.Decisions;
using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Ingest;

/// <summary>A work and what was decided about it.</summary>
public sealed record ResolvedWork(NormalisedWork Work, string Resolution, string? ExistingWorkId);
