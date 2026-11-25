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
/// bounds the body reads. That is <see cref="MaxPendingReads"/> bodies read at once, each up to
/// <see cref="MaxBodyBytes"/> and each within the freshness window; past that a request gets a
/// bare 503 unread.
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

    private readonly SemaphoreSlim _reads = new(
        options.Caps.MaxConcurrent + 2,
        options.Caps.MaxConcurrent + 2
    );

    /// <summary>How many bodies are read at once before their MAC is checked: the runs the launcher may take, and two more.</summary>
    public int MaxPendingReads => options.Caps.MaxConcurrent + 2;

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
    /// The body, read while holding one of <see cref="MaxPendingReads"/> places and within the
    /// freshness window; null, with the status set, when there is no place, it is too large, or it
    /// did not arrive in time.
    /// </summary>
    private async Task<byte[]?> ReadBoundedAsync(HttpContext context)
    {
        if (!await _reads.WaitAsync(TimeSpan.Zero, context.RequestAborted).ConfigureAwait(false))
        {
            logger.LogWarning(
                "Refused a sandbox request unread: {Pending} bodies are already being read.",
                MaxPendingReads
            );
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return null;
        }

        try
        {
            var body = await ReadBodyAsync(context).ConfigureAwait(false);
            if (body is null)
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return body;
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            // Past the window the signature is stale anyway.
            await UnauthorizedAsync(context, "body did not arrive within the freshness window")
                .ConfigureAwait(false);
            return null;
        }
        finally
        {
            _reads.Release();
        }
    }

    private async Task<byte[]?> ReadBodyAsync(HttpContext context)
    {
        if (context.Request.ContentLength is { } declared && declared > MaxBodyBytes)
            return null;
        using var window = new CancellationTokenSource(NonceCache.Window, time);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted,
            window.Token
        );
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
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
                return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), deadline.Token).ConfigureAwait(false);
        }
        return buffer.ToArray();
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
