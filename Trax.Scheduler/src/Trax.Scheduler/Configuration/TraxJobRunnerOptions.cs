using Trax.Scheduler.Services.RequestSigning;

namespace Trax.Scheduler.Configuration;

/// <summary>
/// How a runner decides who may send it work: a signing key, an authorization policy, or an
/// explicit acceptance of unsigned requests. Configured through <c>AddTraxJobRunner(...)</c>.
/// </summary>
/// <remarks>
/// A runner executes whatever it is sent with the trust of the scheduler that dispatched it, so
/// every entry point refuses to start without one of these (see scheduler/0006):
///
/// <list type="table">
/// <listheader><term>Posture</term><description>Applies to</description></listheader>
/// <item><term><see cref="SigningKey"/></term><description>Every entry point. The recommended posture.</description></item>
/// <item><term><see cref="AuthorizationPolicy"/></term><description>The ASP.NET endpoints only (<c>UseTraxJobRunner</c>, <c>UseTraxRunEndpoint</c>).</description></item>
/// <item><term><see cref="AllowUnsignedRequests"/></term><description>Every entry point, logged as a warning at startup.</description></item>
/// </list>
/// </remarks>
public sealed class TraxJobRunnerOptions
{
    /// <summary>
    /// The key shared with the scheduler, at least <see cref="RunnerRequestSignature.MinimumKeyLength"/>
    /// bytes. When set, every request must carry a valid signature made with it, whatever else the
    /// endpoint requires.
    /// </summary>
    public byte[]? SigningKey { get; set; }

    /// <summary>
    /// How far a signed request's timestamp may be from this process's clock, either way, before
    /// it is refused as stale. Also how long a nonce is remembered. Default five minutes.
    /// </summary>
    /// <remarks>
    /// Accepted nonces go to the database by default, so every instance of the runner refuses a
    /// request any of them has accepted. <see cref="UseInMemoryNonceStore"/> keeps them in this
    /// process instead, and a host can register its own <see cref="INonceStore"/>.
    /// </remarks>
    public TimeSpan MaxClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The name of an ASP.NET authorization policy that <c>UseTraxJobRunner</c> and
    /// <c>UseTraxRunEndpoint</c> require. The policy must admit only the scheduler: the runner
    /// runs what it is sent as trusted infrastructure, so a policy that admits end users lets them
    /// run gated trains.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// The largest request body, in bytes, that the HTTP entry points (<c>UseTraxJobRunner</c>,
    /// <c>UseTraxRunEndpoint</c>, and <c>TraxLambdaFunction</c>'s local routes) read. A larger one
    /// is refused with 413 before it runs. Default 8 MiB, which holds a train input at the
    /// mediator's default stored-input cap with room for its escaping. Raise it with the
    /// mediator's <c>MaxInputJsonBytes</c> on the scheduler.
    /// </summary>
    public long MaxRequestBodyBytes { get; set; } = DefaultMaxRequestBodyBytes;

    /// <summary>The default of <see cref="MaxRequestBodyBytes"/>, 8 MiB.</summary>
    public const long DefaultMaxRequestBodyBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Whether <see cref="UseInMemoryNonceStore"/> was called.
    /// </summary>
    public bool InMemoryNonceStore { get; private set; }

    /// <summary>
    /// Keeps the nonces of accepted requests in this process rather than the database. Correct
    /// only for a runner that runs as one instance: each instance keeps its own, so a request sent
    /// to two of them is accepted by both. Needed for a signing key on a host without a relational
    /// data provider, unless the host registers its own <see cref="INonceStore"/>.
    /// </summary>
    /// <returns>These options, for chaining.</returns>
    public TraxJobRunnerOptions UseInMemoryNonceStore()
    {
        InMemoryNonceStore = true;
        return this;
    }

    /// <summary>
    /// Whether <see cref="AllowUnsignedRequests"/> was called.
    /// </summary>
    public bool UnsignedRequestsAllowed { get; private set; }

    /// <summary>
    /// Accepts requests that carry no signature. On an ASP.NET endpoint without an
    /// <see cref="AuthorizationPolicy"/> that means anyone who can reach it can run any registered
    /// train, so each entry point logs a warning when it starts. Suitable for a runner reachable
    /// only by the scheduler, such as a Lambda function or SQS queue guarded by IAM.
    /// </summary>
    /// <returns>These options, for chaining.</returns>
    public TraxJobRunnerOptions AllowUnsignedRequests()
    {
        UnsignedRequestsAllowed = true;
        return this;
    }

    internal void Validate()
    {
        if (SigningKey is not null)
            RunnerRequestSignature.EnsureKey(SigningKey, nameof(SigningKey));

        if (MaxClockSkew <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(MaxClockSkew),
                MaxClockSkew,
                "MaxClockSkew must be positive."
            );

        if (MaxRequestBodyBytes <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(MaxRequestBodyBytes),
                MaxRequestBodyBytes,
                "MaxRequestBodyBytes must be positive."
            );
    }
}
