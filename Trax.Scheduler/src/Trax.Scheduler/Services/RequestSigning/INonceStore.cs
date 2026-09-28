namespace Trax.Scheduler.Services.RequestSigning;

/// <summary>
/// Remembers the nonces a runner has accepted on signed requests, so that a request is accepted
/// once. Every runner instance that can receive the same request must share one store, or each
/// instance accepts it once (see scheduler/0009).
/// </summary>
/// <remarks>
/// <c>AddTraxJobRunner</c> uses the database store when the host has a relational data provider,
/// or <see cref="InMemoryNonceStore"/> after <c>UseInMemoryNonceStore()</c>. A host registers its own
/// implementation as a singleton to share nonces some other way.
/// </remarks>
public interface INonceStore
{
    /// <summary>
    /// Records <paramref name="nonce"/> if no unexpired record of it exists.
    /// </summary>
    /// <param name="nonce">The nonce from a signature whose MAC and timestamp already verified.</param>
    /// <param name="expiresAt">
    /// When the record may be forgotten: after it, the signature's timestamp alone refuses the request.
    /// </param>
    /// <param name="now">The verifier's clock, for judging which records have expired.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>
    /// True when this call recorded the nonce, false when it was already recorded and has not expired.
    /// Two concurrent calls with one nonce must not both return true.
    /// </returns>
    ValueTask<bool> TryRecordAsync(
        string nonce,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken
    );
}
