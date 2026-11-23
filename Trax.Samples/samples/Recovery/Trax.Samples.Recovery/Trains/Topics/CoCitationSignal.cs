using Trax.Core.Decisions;

namespace Trax.Samples.Recovery.Trains.Topics;

/// <summary>
/// The <c>cocitation</c> branch's result: the shared-reference counts and how much the map should
/// trust them, which the track the model's answer chose decides.
/// </summary>
public sealed record CoCitationSignal(string Track, double Weight, IReadOnlyList<PairSignal> Pairs);
