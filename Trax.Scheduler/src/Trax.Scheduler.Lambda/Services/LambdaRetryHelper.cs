using System.Net;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Lambda.Configuration;

namespace Trax.Scheduler.Lambda.Services;

/// <summary>
/// Retries AWS Lambda invocations on transient failures (throttling, 502, 503, 504) with exponential backoff and jitter.
/// </summary>
internal static class LambdaRetryHelper
{
    private static readonly HashSet<HttpStatusCode> TransientStatusCodes =
    [
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    ];

    /// <summary>
    /// Invokes a Lambda function with retry logic for transient AWS failures.
    /// </summary>
    /// <param name="client">The Lambda client to use.</param>
    /// <param name="request">The invocation request.</param>
    /// <param name="options">Retry configuration.</param>
    /// <param name="logger">Logger for retry diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The invoke response (either successful or from the last attempt before a non-transient failure).</returns>
    internal static async Task<InvokeResponse> InvokeWithRetryAsync(
        IAmazonLambda client,
        InvokeRequest request,
        LambdaRetryOptions options,
        ILogger? logger,
        CancellationToken ct
    )
    {
        var maxRetries = Math.Max(0, options.MaxRetries);

        Exception? lastException = null;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await client.InvokeAsync(request, ct);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                lastException = ex;

                if (attempt == maxRetries)
                    break;

                var delay = ComputeDelay(attempt, options);

                logger?.LogWarning(
                    "Lambda invocation failed with transient error ({ErrorType}), retrying in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                    ex.GetType().Name,
                    delay.TotalMilliseconds,
                    attempt + 1,
                    maxRetries
                );

                await Task.Delay(delay, ct);
            }
        }

        throw lastException!;
    }

    /// <summary>
    /// Determines whether an exception represents a transient AWS failure that should be retried.
    /// </summary>
    internal static bool IsTransient(Exception ex)
    {
        if (ex is AmazonServiceException serviceException)
            return TransientStatusCodes.Contains(serviceException.StatusCode);

        if (ex is HttpRequestException)
            return true;

        return false;
    }

    /// <summary>
    /// Computes the delay for a given retry attempt using exponential backoff with jitter.
    /// </summary>
    internal static TimeSpan ComputeDelay(int attempt, LambdaRetryOptions options)
    {
        // Exponential backoff: baseDelay * 2^attempt
        var exponentialMs = options.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt);

        // Add jitter: +/-25%
        var jitterFactor = 0.75 + Random.Shared.NextDouble() * 0.5;
        var delayMs = exponentialMs * jitterFactor;

        return Clamp(TimeSpan.FromMilliseconds(delayMs), options);
    }

    private static TimeSpan Clamp(TimeSpan delay, LambdaRetryOptions options)
    {
        if (delay < TimeSpan.Zero)
            return options.BaseDelay;

        if (delay > options.MaxDelay)
            return options.MaxDelay;

        return delay;
    }
}
