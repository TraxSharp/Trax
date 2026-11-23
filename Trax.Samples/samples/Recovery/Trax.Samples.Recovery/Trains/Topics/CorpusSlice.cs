using Trax.Core.Decisions;

namespace Trax.Samples.Recovery.Trains.Topics;

/// <summary>The papers the run maps: the fields and years its input chose. Every branch reads it.</summary>
public sealed record CorpusSlice(string RunId, IReadOnlyList<Paper> Papers);
