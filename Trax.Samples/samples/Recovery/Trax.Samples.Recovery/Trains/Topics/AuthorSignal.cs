using Trax.Core.Decisions;

namespace Trax.Samples.Recovery.Trains.Topics;

/// <summary>The <c>authors</c> branch's result: the pairs written by at least one author in common.</summary>
public sealed record AuthorSignal(IReadOnlyList<PairSignal> Pairs);
