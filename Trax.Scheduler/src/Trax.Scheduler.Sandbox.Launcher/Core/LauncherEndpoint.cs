using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Core;

/// <summary>
/// The launcher's one route. A request is checked in an order that costs an unauthenticated
/// caller as little as possible: the signature's form and freshness before the body is read, the
/// MAC before the body is parsed, the nonce before anything runs. Until the MAC verifies, the
/// answer is a bare 401; from then on every answer is signed for the request it answers.
/// </summary>
internal sealed partial class LauncherEndpoint(
    SandboxLauncherOptions options,
    NonceCache nonces,
    SandboxRunner runner,
    ILogger<LauncherEndpoint> logger
)
{
    public const string Route = "/trax/sandbox/run";

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

        var body = await ReadBodyAsync(context).ConfigureAwait(false);
        if (body is null)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

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

    private async Task<byte[]?> ReadBodyAsync(HttpContext context)
    {
        if (context.Request.ContentLength is { } declared && declared > MaxBodyBytes)
            return null;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while (
            (
                read = await context
                    .Request.Body.ReadAsync(chunk, context.RequestAborted)
                    .ConfigureAwait(false)
            ) > 0
        )
        {
            if (buffer.Length + read > MaxBodyBytes)
                return null;
            await buffer
                .WriteAsync(chunk.AsMemory(0, read), context.RequestAborted)
                .ConfigureAwait(false);
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
