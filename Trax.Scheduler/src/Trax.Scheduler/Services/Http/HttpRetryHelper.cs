using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.RequestSigning;

namespace Trax.Scheduler.Services.Http;

/// <summary>
/// Retries HTTP POST requests on transient status codes (429, 502, 503) with exponential backoff and jitter.
/// </summary>
internal static class HttpRetryHelper
{
    private static readonly HashSet<HttpStatusCode> TransientStatusCodes =
    [
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
    ];

    /// <summary>
    /// Posts a JSON request with retry logic for transient HTTP failures.
    /// </summary>
    /// <param name="client">The HTTP client to use.</param>
    /// <param name="request">The request body to serialize as JSON.</param>
    /// <param name="options">Retry configuration.</param>
    /// <param name="logger">Logger for retry diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The final HTTP response (either successful or the last failed attempt).</returns>
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static readonly MediaTypeHeaderValue JsonContentType = new("application/json")
    {
        CharSet = "utf-8",
    };

    internal static async Task<HttpResponseMessage> PostWithRetryAsync(
        HttpClient client,
        object request,
        HttpRetryOptions options,
        ILogger? logger,
        CancellationToken ct,
        byte[]? signingKey = null,
        RunnerRequestPurpose purpose = RunnerRequestPurpose.Execute
    )
    {
        var maxRetries = Math.Max(0, options.MaxRetries);

        // Serialized once, as PostAsJsonAsync would, so the bytes signed are the bytes sent.
        var body = JsonSerializer.SerializeToUtf8Bytes(request, request.GetType(), WebJson);

        HttpResponseMessage? response = null;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            response?.Dispose();

            // Signed per attempt: each retry carries its own timestamp and nonce, so a runner
            // that remembers the first attempt's nonce does not refuse the retry as a replay.
            using var message = new HttpRequestMessage(HttpMethod.Post, string.Empty)
            {
                Content = new ByteArrayContent(body)
                {
                    Headers = { ContentType = JsonContentType },
                },
            };
            if (signingKey is not null)
                message.Headers.TryAddWithoutValidation(
                    RunnerRequestSignature.HeaderName,
                    RunnerRequestSignature.Create(signingKey, purpose, body)
                );

            response = await client.SendAsync(message, ct);

            if (!TransientStatusCodes.Contains(response.StatusCode))
                return response;

            if (attempt == maxRetries)
                break;

            var delay = ComputeDelay(attempt, response, options);

            logger?.LogWarning(
                "HTTP {StatusCode} from remote endpoint, retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                (int)response.StatusCode,
                delay.TotalMilliseconds,
                attempt + 1,
                maxRetries
            );

            await Task.Delay(delay, ct);
        }

        return response!;
    }

    /// <summary>
    /// Computes the delay for a given retry attempt, respecting Retry-After header if present.
    /// </summary>
    internal static TimeSpan ComputeDelay(
        int attempt,
        HttpResponseMessage response,
        HttpRetryOptions options
    )
    {
        // Respect Retry-After header if the server provides one
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta)
                return Clamp(delta, options);

            if (retryAfter.Date is { } date)
            {
                var serverDelay = date - DateTimeOffset.UtcNow;
                if (serverDelay > TimeSpan.Zero)
                    return Clamp(serverDelay, options);
            }
        }

        // Exponential backoff: baseDelay * 2^attempt
        var exponentialMs = options.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt);

        // Add jitter: ±25%
        var jitterFactor = 0.75 + Random.Shared.NextDouble() * 0.5;
        var delayMs = exponentialMs * jitterFactor;

        return Clamp(TimeSpan.FromMilliseconds(delayMs), options);
    }

    private static TimeSpan Clamp(TimeSpan delay, HttpRetryOptions options)
    {
        if (delay < TimeSpan.Zero)
            return options.BaseDelay;

        if (delay > options.MaxDelay)
            return options.MaxDelay;

        return delay;
    }
}
