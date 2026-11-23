using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Recovery.Trains.Discover;

/// <summary>One partition of an index: a source, a month as <c>yyyy-MM</c>, and how many records it holds.</summary>
public sealed record SourcePartition(string Source, string Month, int Records);
