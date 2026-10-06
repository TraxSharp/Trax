using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Services.RequestSigning;

/// <summary>
/// The outcome of <see cref="RunnerRequestVerifier.VerifyAsync"/>.
/// </summary>
public enum RunnerRequestVerdict
{
    /// <summary>The request may run.</summary>
    Accepted,

    /// <summary>A signing key is configured and the request carries no signature.</summary>
    Missing,

    /// <summary>The signature is malformed or does not match the body.</summary>
    Invalid,

    /// <summary>The signature's timestamp is further from this clock than the allowed skew.</summary>
    Stale,

    /// <summary>The signature's nonce was already accepted inside the skew window.</summary>
    Replayed,
}

/// <summary>
/// Checks a runner request against the posture in <see cref="TraxJobRunnerOptions"/>. Registered
/// as a singleton by <c>AddTraxJobRunner</c>, because the nonces it remembers must outlive a
/// request.
/// </summary>
/// <remarks>
/// Public because Trax.Runner.Lambda, which ships separately, verifies with it. The nonces it has
/// accepted are kept in an <see cref="INonceStore"/>, which instances of one runner share so that a
/// request is accepted once across all of them (see scheduler/0009).
/// </remarks>
public sealed class RunnerRequestVerifier
{
    private readonly TraxJobRunnerOptions _options;
    private readonly ILogger<RunnerRequestVerifier> _logger;
    private readonly TimeProvider _time;
    private readonly INonceStore? _nonces;
    private readonly ConcurrentDictionary<string, bool> _warnedEntryPoints = new(
        StringComparer.Ordinal
    );
    private readonly object _refusalLog = new();
    private DateTimeOffset _lastRefusalWarning = DateTimeOffset.MinValue;
    private int _refusalsSinceWarning;

    /// <summary>How often a refused request is logged as a warning; the rest go to Debug.</summary>
    internal static readonly TimeSpan RefusalWarningInterval = TimeSpan.FromMinutes(1);

    /// <summary>Creates a verifier over <paramref name="options"/>.</summary>
    /// <param name="options">The runner's posture.</param>
    /// <param name="logger">Where refusals and posture warnings go.</param>
    /// <param name="nonces">
    /// Where accepted nonces are kept. Required when <paramref name="options"/> has a signing key,
    /// and shared by every instance of the runner.
    /// </param>
    /// <param name="time">The clock freshness is judged by.</param>
    /// <exception cref="ArgumentException">A signing key is set and <paramref name="nonces"/> is null.</exception>
    public RunnerRequestVerifier(
        TraxJobRunnerOptions options,
        ILogger<RunnerRequestVerifier> logger,
        INonceStore? nonces = null,
        TimeProvider? time = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        options.Validate();
        if (options.SigningKey is not null && nonces is null)
            throw new ArgumentException(
                "A runner with a SigningKey needs an INonceStore to refuse a repeated request.",
                nameof(nonces)
            );
        _options = options;
        _logger = logger;
        _nonces = nonces;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Refuses to start an entry point that has no posture of its own: one that cannot apply an
    /// ASP.NET policy (an SQS handler, a Lambda function) needs a signing key or an explicit
    /// <see cref="TraxJobRunnerOptions.AllowUnsignedRequests"/>. Logs a warning, once per entry
    /// point, when it will accept unsigned requests.
    /// </summary>
    /// <param name="entryPoint">A name for the entry point, used in the error and the warning.</param>
    /// <exception cref="InvalidOperationException">No posture is configured.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void EnsurePosture(string entryPoint) => EnsurePosture(entryPoint, policyApplies: false);

    internal void EnsurePosture(string entryPoint, bool policyApplies)
    {
        if (_options.SigningKey is not null)
            return;

        if (policyApplies && _options.AuthorizationPolicy is not null)
            return;

        if (!_options.UnsignedRequestsAllowed)
            throw new InvalidOperationException(
                $"The Trax runner entry point '{entryPoint}' has no authorization posture. "
                    + "Pass AddTraxJobRunner(runner => ...) a SigningKey shared with the scheduler"
                    + (
                        policyApplies
                            ? ", an AuthorizationPolicy that admits only the scheduler,"
                            : ""
                    )
                    + " or call AllowUnsignedRequests() to accept requests from anyone who can reach it."
            );

        if (_warnedEntryPoints.TryAdd(entryPoint, true))
            _logger.LogWarning(
                "Trax runner entry point {EntryPoint} accepts unsigned requests (AllowUnsignedRequests). "
                    + "Anyone who can reach it can run any registered train.",
                entryPoint
            );
    }

    /// <summary>
    /// Whether every request must carry a signature, because a signing key is configured.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool RequiresSignature => _options.SigningKey is not null;

    /// <summary>
    /// Checks what can be checked of <paramref name="signature"/> without the body: that it is
    /// present and well formed, and, when <paramref name="requireFresh"/>, that its timestamp is
    /// within <see cref="TraxJobRunnerOptions.MaxClockSkew"/>. With no signing key configured,
    /// <see cref="RunnerRequestVerdict.Accepted"/>. An accepted header still needs
    /// <see cref="VerifyAsync"/> over the body.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public RunnerRequestVerdict CheckHeader(string? signature, bool requireFresh)
    {
        if (_options.SigningKey is null)
            return RunnerRequestVerdict.Accepted;

        if (string.IsNullOrEmpty(signature))
            return RunnerRequestVerdict.Missing;

        if (!RunnerRequestSignature.TryReadTimestamp(signature, out var timestamp))
            return RunnerRequestVerdict.Invalid;

        if (
            requireFresh
            && Math.Abs(_time.GetUtcNow().ToUnixTimeSeconds() - timestamp)
                > (long)_options.MaxClockSkew.TotalSeconds
        )
            return RunnerRequestVerdict.Stale;

        return RunnerRequestVerdict.Accepted;
    }

    /// <summary>
    /// Reads and verifies the body of an HTTP runner request, or says which status refuses it.
    /// The <c>Trax-Signature</c> header is checked first (<see cref="CheckHeader"/>), so a
    /// request without a usable one is refused with 401 before any of its body is read. The body
    /// is then read up to <see cref="TraxJobRunnerOptions.MaxRequestBodyBytes"/>, 413 past it,
    /// and its signature verified, 401 when refused. Every refusal is reported through
    /// <see cref="ReportRefusal"/>. Used by both ASP.NET endpoints and by Trax.Runner.Lambda's
    /// local routes.
    /// </summary>
    /// <returns>The body, or the status code to answer with and no body.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public async Task<(ReadOnlyMemory<byte> Body, int? RefusedStatusCode)> ReadVerifiedBodyAsync(
        HttpRequest request,
        RunnerRequestPurpose purpose
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var signature = request.Headers[RunnerRequestSignature.HeaderName].ToString();
        var headerVerdict = CheckHeader(signature, requireFresh: true);
        if (headerVerdict != RunnerRequestVerdict.Accepted)
        {
            ReportRefusal(request.Path, $"signature {headerVerdict}");
            return (default, StatusCodes.Status401Unauthorized);
        }

        var body = await ReadBoundedAsync(request, _options.MaxRequestBodyBytes);
        if (body is null)
        {
            ReportRefusal(request.Path, "body over MaxRequestBodyBytes");
            return (default, StatusCodes.Status413PayloadTooLarge);
        }

        var verdict = await VerifyAsync(
            purpose,
            body.Value,
            signature,
            requireFresh: true,
            request.HttpContext.RequestAborted
        );
        if (verdict != RunnerRequestVerdict.Accepted)
        {
            ReportRefusal(request.Path, $"signature {verdict}");
            return (default, StatusCodes.Status401Unauthorized);
        }

        return (body.Value, null);
    }

    /// <summary>
    /// Logs a refused request: as a warning at most once per minute, counting the refusals since
    /// the last one, and at Debug otherwise, so a stream of refused requests cannot fill the log.
    /// </summary>
    /// <param name="entryPoint">The path or entry point that refused it.</param>
    /// <param name="reason">Why, for the log.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void ReportRefusal(string entryPoint, string reason)
    {
        int earlier;
        lock (_refusalLog)
        {
            var now = _time.GetUtcNow();
            if (now - _lastRefusalWarning < RefusalWarningInterval)
            {
                _refusalsSinceWarning++;
                earlier = -1;
            }
            else
            {
                earlier = _refusalsSinceWarning;
                _refusalsSinceWarning = 0;
                _lastRefusalWarning = now;
            }
        }

        if (earlier < 0)
            _logger.LogDebug(
                "Refused a Trax runner request to {EntryPoint}: {Reason}",
                entryPoint,
                reason
            );
        else
            _logger.LogWarning(
                "Refused a Trax runner request to {EntryPoint}: {Reason}. {Earlier} more were "
                    + "refused since the last of these warnings, which are logged at most once a "
                    + "minute; the rest are at Debug.",
                entryPoint,
                reason,
                earlier
            );
    }

    /// <summary>
    /// The body, or null when it is larger than <paramref name="limit"/>. The declared length is
    /// checked before anything is read, the server's own limit is lowered to match where it can
    /// be, and the read stops at the limit whatever the request declared.
    /// </summary>
    private static async Task<ReadOnlyMemory<byte>?> ReadBoundedAsync(
        HttpRequest request,
        long limit
    )
    {
        if (request.ContentLength > limit)
            return null;

        if (
            request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>()
                is { IsReadOnly: false } sizeFeature
            && (sizeFeature.MaxRequestBodySize is null || sizeFeature.MaxRequestBodySize > limit)
        )
            sizeFeature.MaxRequestBodySize = limit;

        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int read;
            while (
                (read = await request.Body.ReadAsync(chunk, request.HttpContext.RequestAborted)) > 0
            )
            {
                if (buffer.Length + read > limit)
                    return null;

                buffer.Write(chunk, 0, read);
            }
        }
        catch (BadHttpRequestException ex)
            when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Verifies <paramref name="signature"/> over <paramref name="body"/>. With no signing key
    /// configured, every request is <see cref="RunnerRequestVerdict.Accepted"/>: the entry point's
    /// posture was checked when it started.
    /// </summary>
    /// <param name="purpose">What the request is for.</param>
    /// <param name="body">The exact bytes received.</param>
    /// <param name="signature">The signature received, or null.</param>
    /// <param name="requireFresh">
    /// Whether to refuse a stale timestamp or a repeated nonce. False only for transports that
    /// redeliver the same message by design (SQS, an asynchronous Lambda invocation), where the
    /// job's Pending metadata row is what stops a second run.
    /// </param>
    /// <param name="cancellationToken">Cancels the nonce store's check.</param>
    public async ValueTask<RunnerRequestVerdict> VerifyAsync(
        RunnerRequestPurpose purpose,
        ReadOnlyMemory<byte> body,
        string? signature,
        bool requireFresh,
        CancellationToken cancellationToken = default
    )
    {
        var key = _options.SigningKey;
        if (key is null)
            return RunnerRequestVerdict.Accepted;

        if (string.IsNullOrEmpty(signature))
            return RunnerRequestVerdict.Missing;

        if (
            !RunnerRequestSignature.TryVerifyMac(
                key,
                purpose,
                body.Span,
                signature,
                out var timestamp,
                out var nonce
            )
        )
            return RunnerRequestVerdict.Invalid;

        if (!requireFresh)
            return RunnerRequestVerdict.Accepted;

        var now = _time.GetUtcNow();
        var skew = (long)_options.MaxClockSkew.TotalSeconds;
        if (Math.Abs(now.ToUnixTimeSeconds() - timestamp) > skew)
            return RunnerRequestVerdict.Stale;

        // Remembered until the timestamp itself goes stale; after that freshness refuses it.
        var recorded = await _nonces!.TryRecordAsync(
            nonce,
            DateTimeOffset.FromUnixTimeSeconds(timestamp + skew),
            now,
            cancellationToken
        );

        return recorded ? RunnerRequestVerdict.Accepted : RunnerRequestVerdict.Replayed;
    }
}
