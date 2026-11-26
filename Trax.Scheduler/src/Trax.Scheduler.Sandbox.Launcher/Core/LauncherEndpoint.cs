using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Core;

/// <summary>
/// The launcher's one route. A request is checked in an order that costs an unauthenticated
/// caller as little as it can: the signature's form and freshness before the body is read, the
/// MAC before the body is parsed, the nonce before anything runs. Until the MAC verifies, the
/// answer is a bare 401; from then on every answer is signed for the request it answers.
/// </summary>
/// <remarks>
/// The form and freshness check costs a caller without the key nothing to pass, so it is not what
/// bounds the body reads. Bytes and time are: every body not yet verified draws on one budget of
/// <see cref="MaxUnverifiedBytes"/> as its bytes arrive, so a request that sends nothing holds
/// nothing; and a body must keep arriving at <see cref="MinBodyBytesPerSecond"/> after a
/// <see cref="BodyGrace"/>, so one that stalls is dropped with a bare 408 within seconds rather than
/// at the end of the freshness window. A body the budget cannot take gets a bare 503 with
/// <c>Retry-After</c>. Holding the budget therefore costs a caller without the key a steady stream
/// of bytes, not a handful of idle connections.
/// </remarks>
internal sealed partial class LauncherEndpoint(
    SandboxLauncherOptions options,
    NonceCache nonces,
    SandboxRunner runner,
    TimeProvider time,
    ILogger<LauncherEndpoint> logger
)
{
    public const string Route = "/trax/sandbox/run";

    /// <summary>How long a body may take to start arriving before it must keep pace.</summary>
    public static readonly TimeSpan BodyGrace = TimeSpan.FromSeconds(5);

    /// <summary>The slowest a body may arrive after <see cref="BodyGrace"/>: 1 MiB a second, which a hub on the same node or cluster network beats by far.</summary>
    public const long MinBodyBytesPerSecond = 1024 * 1024;

    private long _unverified;

    /// <summary>
    /// The most bytes of bodies not yet verified held at once, by every request together: as many
    /// largest bodies as the runs the launcher may take, and two more.
    /// </summary>
    public long MaxUnverifiedBytes => (options.Caps.MaxConcurrent + 2) * MaxBodyBytes;

    /// <summary>The bytes of bodies not yet verified held now.</summary>
    internal long UnverifiedBytes => Interlocked.Read(ref _unverified);

    /// <summary>The largest body read: the input cap as JSON-escaped text (six bytes a character at worst), plus the rest of the request.</summary>
    public long MaxBodyBytes => (long)options.Caps.MaxInputBytes * 6 + 64 * 1024;

    public async Task HandleAsync(HttpContext context)
    {
        var key = options.SigningKey ?? throw new InvalidOperationException("No signing key.");
        var signature = context.Request.Headers[SandboxChannelSignature.HeaderName].ToString();

        if (
            !SandboxChannelSignature.TryReadTimestamp(signature, out var timestamp)
            || !nonces.IsFresh(timestamp)
        )
        {
            await UnauthorizedAsync(context, "missing, malformed or stale signature")
                .ConfigureAwait(false);
            return;
        }

        var body = await ReadBoundedAsync(context).ConfigureAwait(false);
        if (body is null)
            return;

        if (!SandboxChannelSignature.TryVerify(key, body, signature, out timestamp, out var nonce))
        {
            await UnauthorizedAsync(context, "signature does not match").ConfigureAwait(false);
            return;
        }

        if (!nonces.TryAccept(nonce, timestamp))
        {
            await UnauthorizedAsync(context, "request was already accepted once")
                .ConfigureAwait(false);
            return;
        }

        var (request, profile, refused) = SandboxRequestReader.Read(body, options);
        SandboxRunResponse answer;
        if (request is null || profile is null)
        {
            logger.LogWarning("Refused a signed sandbox request: {Reason}", refused);
            answer = new SandboxRunResponse(EchoRunId(body), SandboxOutcome.Refused, refused);
        }
        else
            answer = await runner
                .RunAsync(request, profile, context.RequestAborted)
                .ConfigureAwait(false);

        await WriteAsync(context, key, signature, answer).ConfigureAwait(false);
    }

    /// <summary>
    /// The body, read against the shared budget and the pace a body must keep; null, with the
    /// status set, when it is too large, the budget cannot take it, or it did not keep pace.
    /// </summary>
    private async Task<byte[]?> ReadBoundedAsync(HttpContext context)
    {
        if (context.Request.ContentLength is { } declared)
        {
            if (declared > MaxBodyBytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return null;
            }
            if (declared > MaxUnverifiedBytes - UnverifiedBytes)
                return Busy(context);
        }

        var held = new StrongBox<long>();
        try
        {
            return await ReadPacedAsync(context, held).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(
                "Dropped an unauthenticated sandbox request: its body did not keep pace ({Bytes} bytes arrived).",
                held.Value
            );
            context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
            return null;
        }
        finally
        {
            Interlocked.Add(ref _unverified, -held.Value);
        }
    }

    /// <summary>The body, each chunk taken from the budget into <paramref name="held"/> as it arrives; cancelled when it falls behind its pace.</summary>
    private async Task<byte[]?> ReadPacedAsync(HttpContext context, StrongBox<long> held)
    {
        var chunk = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            var start = time.GetUtcNow();
            using var pace = new CancellationTokenSource(Pace(0), time);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
                context.RequestAborted,
                pace.Token
            );
            using var buffer = new MemoryStream();
            int read;
            while (
                (
                    read = await context
                        .Request.Body.ReadAsync(chunk, deadline.Token)
                        .ConfigureAwait(false)
                ) > 0
            )
            {
                if (buffer.Length + read > MaxBodyBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return null;
                }
                if (Interlocked.Add(ref _unverified, read) > MaxUnverifiedBytes)
                {
                    Interlocked.Add(ref _unverified, -read);
                    return Busy(context);
                }
                held.Value += read;
                await buffer
                    .WriteAsync(chunk.AsMemory(0, read), deadline.Token)
                    .ConfigureAwait(false);
                var left = Pace(held.Value) - (time.GetUtcNow() - start);
                if (left <= TimeSpan.Zero)
                    await pace.CancelAsync().ConfigureAwait(false);
                else
                    pace.CancelAfter(left);
            }
            return buffer.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    /// <summary>
    /// How long after its start a body that has sent <paramref name="received"/> bytes may wait for
    /// more: the grace, then the time those bytes earn at the minimum rate, and never past the
    /// freshness window, beyond which the signature is stale anyway.
    /// </summary>
    private static TimeSpan Pace(long received)
    {
        var earned = BodyGrace + TimeSpan.FromSeconds((double)received / MinBodyBytesPerSecond);
        return earned < NonceCache.Window ? earned : NonceCache.Window;
    }

    private byte[]? Busy(HttpContext context)
    {
        logger.LogWarning(
            "Turned a sandbox request away unread: {Bytes} bytes of unverified bodies are already held.",
            UnverifiedBytes
        );
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "1";
        return null;
    }

    private Task UnauthorizedAsync(HttpContext context, string why)
    {
        logger.LogWarning("Refused an unauthenticated sandbox request: {Reason}", why);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    private static async Task WriteAsync(
        HttpContext context,
        byte[] key,
        string requestSignature,
        SandboxRunResponse answer
    )
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(answer, SandboxJson.Options);
        context.Response.StatusCode = answer.Outcome switch
        {
            SandboxOutcome.Refused => StatusCodes.Status400BadRequest,
            SandboxOutcome.Busy => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status200OK,
        };
        context.Response.Headers[SandboxChannelSignature.ResponseHeaderName] =
            SandboxChannelSignature.SignResponse(key, requestSignature, bytes);
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>The request's run id when it has a well-formed one, so a refusal still names the run; else empty.</summary>
    private static string EchoRunId(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("runId", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() is { } text
                && RunId.IsMatch(text)
                ? text
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex RunId { get; }
}
