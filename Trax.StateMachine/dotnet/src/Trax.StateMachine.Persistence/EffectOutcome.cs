namespace Trax.StateMachine.Persistence;

/// <summary>
/// The result of <see cref="IdempotentEffect.RunOnce(string, Func{Task{string}}, TimeSpan?, CancellationToken)"/>: the effect ran now (Ran), its recorded result
/// was replayed (AlreadyRan — a concurrent/retried caller), or another caller holds the claim and is
/// still in flight (InProgress).
/// </summary>
public abstract record EffectOutcome
{
    /// <summary>This call ran the effect exactly once; <see cref="Receipt"/> is its result.</summary>
    public sealed record Ran(string Receipt) : EffectOutcome;

    /// <summary>The effect already ran; <see cref="Receipt"/> is the recorded result, handed back without re-running.</summary>
    public sealed record AlreadyRan(string Receipt) : EffectOutcome
    {
        /// <summary>
        /// The fingerprint of the content the effect ran on, as its claim recorded it, or null when the claim
        /// recorded none. A caller that passed a fingerprint compares the two before using <see cref="Receipt"/>.
        /// </summary>
        public string? ContentFingerprint { get; init; }
    }

    /// <summary>Another caller holds the claim and is mid-flight (no receipt yet). This call did NOT run.</summary>
    public sealed record InProgress : EffectOutcome;

    private EffectOutcome() { }
}
