using Trax.Core.Decisions;

namespace Trax.Samples.Recovery.Trains.Topics;

/// <summary>One signal's value for two papers, <see cref="WorkA"/> being the lower id.</summary>
public sealed record PairSignal(string WorkA, string WorkB, double Value);
