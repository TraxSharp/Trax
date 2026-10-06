using Trax.Core.Decisions;

namespace Trax.Effect.Data.Decisions;

/// <summary>
/// How <c>AddDecisionRecording</c> records and replays decisions. Configure it with
/// <c>AddDecisionRecording(o =&gt; ...)</c>.
/// </summary>
public sealed class DecisionRecordingOptions
{
    /// <summary>The default for <see cref="ReplayAnswersFor"/>: 24 hours.</summary>
    public static readonly TimeSpan DefaultMaxReplayAge = TimeSpan.FromHours(24);

    internal DecisionRecordingOptions() { }

    /// <summary>How long after a decider gave an answer it is still replayed.</summary>
    public TimeSpan MaxReplayAge { get; private set; } = DefaultMaxReplayAge;

    /// <summary>
    /// Replays a recorded answer into a repeated run (a requeue, a manifest's retry) only while it
    /// is younger than <paramref name="maxAge"/>, measured from when a decider gave it, not from a
    /// later run that replayed it. An older one is asked afresh and the decision logged. Defaults to
    /// <see cref="DefaultMaxReplayAge"/>.
    /// </summary>
    /// <param name="maxAge">
    /// At least one second. <see cref="TimeSpan.MaxValue"/>, or any span longer than the calendar
    /// goes back, replays answers of any age.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAge"/> is under a second.</exception>
    public DecisionRecordingOptions ReplayAnswersFor(TimeSpan maxAge)
    {
        if (maxAge < TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(
                nameof(maxAge),
                maxAge,
                "ReplayAnswersFor() requires at least one second. Omit the call to replay answers "
                    + $"for the default of {DefaultMaxReplayAge}."
            );

        MaxReplayAge = maxAge;
        return this;
    }

    /// <summary>
    /// The configuration key <c>AddDecisionRecording</c> reads a state hash key from, base64, when
    /// <see cref="HashStatesWith"/> was not called: <c>Trax:Decisions:StateHashKey</c>.
    /// </summary>
    public const string StateHashKeyConfigurationKey = "Trax:Decisions:StateHashKey";

    /// <summary>The key given to <see cref="HashStatesWith"/>, or null.</summary>
    internal StateHashKey? StateHashKey { get; private set; }

    /// <summary>
    /// Keys the hash of each decision's state with <paramref name="key"/>: an HMAC-SHA256 under it,
    /// which only a holder of the key can compute, instead of a plain SHA-256. Without this call
    /// the key is read, base64, from configuration under <see cref="StateHashKeyConfigurationKey"/>,
    /// and without either the hash is unkeyed.
    /// </summary>
    /// <remarks>
    /// Every process that may repeat a run must use the same key: an answer recorded under one key,
    /// or none, is never replayed under another, so changing it means the next repeated run asks
    /// afresh, once. Keep it as the host keeps its other secrets. Without a key, the journal records
    /// no state hash for a question about a state that reaches a member marked
    /// <c>[TraxSensitive]</c>, so such an answer is never replayed, and logs a warning saying so.
    /// </remarks>
    /// <param name="key">At least 32 bytes, best drawn from a cryptographic random source.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null or shorter than 32 bytes.</exception>
    public DecisionRecordingOptions HashStatesWith(byte[] key)
    {
        StateHashKey = new StateHashKey(key);
        return this;
    }
}
