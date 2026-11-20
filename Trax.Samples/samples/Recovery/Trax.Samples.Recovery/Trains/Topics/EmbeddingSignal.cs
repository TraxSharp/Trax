using Trax.Core.Decisions;

namespace Trax.Samples.Recovery.Trains.Topics;

/// <summary>The <c>embedding</c> branch's result: how alike every two abstracts read, from 0 to 1.</summary>
public sealed record EmbeddingSignal(IReadOnlyList<PairSignal> Pairs);
