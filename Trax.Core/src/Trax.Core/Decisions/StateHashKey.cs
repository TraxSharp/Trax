namespace Trax.Core.Decisions;

/// <summary>
/// The secret a host registers in its container so the hash of each decision's state
/// (<see cref="DecisionMade.StateHash"/>) is keyed: an HMAC-SHA256 under this key, prefixed
/// <c>k1:</c>, rather than a plain SHA-256, prefixed <c>s1:</c>.
/// </summary>
/// <remarks>
/// The hash covers every value the state holds, including members a host masks or withholds
/// elsewhere, and a host stores it with each answer. Keyed, it can only be computed by whoever
/// holds the key, so a host that records decisions should register one, and keep it as it keeps
/// its other secrets.
///
/// <para>Hashes are compared exactly, so an answer recorded under one key, or without one, is
/// never replayed under another: changing or adding the key means the next repeated run asks its
/// deciders afresh, once. Every process that may repeat a run must use the same key.</para>
/// </remarks>
public sealed class StateHashKey
{
    /// <summary>The fewest bytes a key may have.</summary>
    internal const int MinimumLength = 32;

    /// <summary>A key holding a copy of <paramref name="key"/>.</summary>
    /// <param name="key">At least 32 bytes, best drawn from a cryptographic random source.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null or shorter than 32 bytes.</exception>
    public StateHashKey(byte[] key)
    {
        if (key is null || key.Length < MinimumLength)
            throw new ArgumentException(
                $"A state hash key must be at least {MinimumLength} bytes.",
                nameof(key)
            );

        Key = (byte[])key.Clone();
    }

    internal byte[] Key { get; }
}
