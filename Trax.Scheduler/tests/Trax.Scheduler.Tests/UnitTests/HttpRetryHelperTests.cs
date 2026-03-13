using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Http;

namespace Trax.Scheduler.Tests.UnitTests;

[TestFixture]
public class HttpRetryHelperTests
{
    #region Retry on Transient Status Codes

    [Test]
    public async Task PostWithRetryAsync_429ThenSuccess_RetriesAndReturnsSuccess()
    {
        var handler = new SequentialHandler([HttpStatusCode.TooManyRequests, HttpStatusCode.OK]);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var options = new HttpRetryOptions
        {
            MaxRetries = 3,
            BaseDelay = TimeSpan.FromMilliseconds(1),
        };

        var response = await HttpRetryHelper.PostWithRetryAsync(
            client,
            new { },
            options,
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.RequestCount.Should().Be(2);
    }

    [Test]
    public async Task PostWithRetryAsync_502ThenSuccess_RetriesAndReturnsSuccess()
    {
        var handler = new SequentialHandler([HttpStatusCode.BadGateway, HttpStatusCode.OK]);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var options = new HttpRetryOptions
        {
            MaxRetries = 3,
            BaseDelay = TimeSpan.FromMilliseconds(1),
        };

        var response = await HttpRetryHelper.PostWithRetryAsync(
            client,
            new { },
            options,
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.RequestCount.Should().Be(2);
    }

    [Test]
    public async Task PostWithRetryAsync_503ThenSuccess_RetriesAndReturnsSuccess()
    {
        var handler = new SequentialHandler([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK]);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var options = new HttpRetryOptions
        {
            MaxRetries = 3,
            BaseDelay = TimeSpan.FromMilliseconds(1),
        };

        var response = await HttpRetryHelper.PostWithRetryAsync(
            client,
            new { },
            options,
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.RequestCount.Should().Be(2);
    }

    [Test]
    public async Task PostWithRetryAsync_Multiple429ThenSuccess_RetriesMultipleTimes()
    {
        var handler = new SequentialHandler([
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.OK,
        ]);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var options = new HttpRetryOptions
        {
            MaxRetries = 5,
            BaseDelay = TimeSpan.FromMilliseconds(1),
        };

        var response = await HttpRetryHelper.PostWithRetryAsync(
            client,
            new { },
            options,
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.RequestCount.Should().Be(4);
    }

    #endregion

    #region Exhaust Retries

    [Test]
    public async Task PostWithRetryAsync_429ExceedsMaxRetries_ReturnsLastFailedResponse()
    {
        var handler = new SequentialHandler([
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests,
        ]);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var options = new HttpRetryOptions
        {
            MaxRetries = 3,
            BaseDelay = TimeSpan.FromMilliseconds(1),
        };

        var response = await HttpRetryHelper.PostWithRetryAsync(
            client,
            new { },
            options,
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        handler.RequestCount.Should().Be(4); // 1 initial + 3 retries
    }

    #endregion

    #region Non-Transient Status Codes

    [Test]
    public async Task PostWithRetryAsync_500_DoesNotRetry()
    {
        var handler = new SequentialHandler([HttpStatusCode.InternalServerError]);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var options = new HttpRetryOptions
        {
            MaxRetries = 3,
            BaseDelay = TimeSpan.FromMilliseconds(1),
        };

        var response = await HttpRetryHelper.PostWithRetryAsync(
            client,
            new { },
            options,
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        handler.RequestCount.Should().Be(1);
    }

    [Test]
    public async Task PostWithRetryAsync_200_DoesNotRetry()
    {
        var handler = new SequentialHandler([HttpStatusCode.OK]);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var options = new HttpRetryOptions
        {
            MaxRetries = 3,
            BaseDelay = TimeSpan.FromMilliseconds(1),
        };

        var response = await HttpRetryHelper.PostWithRetryAsync(
            client,
            new { },
            options,
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.RequestCount.Should().Be(1);
    }

    [Test]
    public async Task PostWithRetryAsync_404_DoesNotRetry()
    {
        var handler = new SequentialHandler([HttpStatusCode.NotFound]);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var options = new HttpRetryOptions
        {
            MaxRetries = 3,
            BaseDelay = TimeSpan.FromMilliseconds(1),
        };

        var response = await HttpRetryHelper.PostWithRetryAsync(
            client,
            new { },
            options,
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        handler.RequestCount.Should().Be(1);
    }

    #endregion

    #region MaxRetries = 0 (Disabled)

    [Test]
    public async Task PostWithRetryAsync_MaxRetriesZero_DoesNotRetry()
    {
        var handler = new SequentialHandler([HttpStatusCode.TooManyRequests, HttpStatusCode.OK]);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var options = new HttpRetryOptions
        {
            MaxRetries = 0,
            BaseDelay = TimeSpan.FromMilliseconds(1),
        };

        var response = await HttpRetryHelper.PostWithRetryAsync(
            client,
            new { },
            options,
            NullLogger.Instance,
            CancellationToken.None
        );

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        handler.RequestCount.Should().Be(1);
    }

    #endregion

    #region ComputeDelay

    [Test]
    public void ComputeDelay_Attempt0_ReturnsAroundBaseDelay()
    {
        var options = new HttpRetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
        };
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var delay = HttpRetryHelper.ComputeDelay(0, response, options);

        // Base delay * 2^0 = 1s, with ±25% jitter → 0.75s to 1.25s
        delay.TotalMilliseconds.Should().BeInRange(750, 1250);
    }

    [Test]
    public void ComputeDelay_Attempt3_ReturnsExponentiallyHigher()
    {
        var options = new HttpRetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
        };
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var delay = HttpRetryHelper.ComputeDelay(3, response, options);

        // Base delay * 2^3 = 8s, with ±25% jitter → 6s to 10s
        delay.TotalMilliseconds.Should().BeInRange(6000, 10000);
    }

    [Test]
    public void ComputeDelay_LargeAttempt_CapsAtMaxDelay()
    {
        var options = new HttpRetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
        };
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var delay = HttpRetryHelper.ComputeDelay(10, response, options);

        delay.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void ComputeDelay_WithRetryAfterDelta_RespectsServerDelay()
    {
        var options = new HttpRetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(60),
        };
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));

        var delay = HttpRetryHelper.ComputeDelay(0, response, options);

        delay.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void ComputeDelay_WithRetryAfterDelta_ClampedToMaxDelay()
    {
        var options = new HttpRetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(10),
        };
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));

        var delay = HttpRetryHelper.ComputeDelay(0, response, options);

        delay.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Test]
    public void ComputeDelay_JitterApplied_ProducesDifferentValues()
    {
        var options = new HttpRetryOptions
        {
            BaseDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(30),
        };
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var delays = Enumerable
            .Range(0, 20)
            .Select(_ => HttpRetryHelper.ComputeDelay(2, response, options))
            .ToList();

        // With jitter, we should get at least some variation
        delays.Distinct().Count().Should().BeGreaterThan(1);
    }

    #endregion

    #region Cancellation

    [Test]
    public async Task PostWithRetryAsync_CancelledDuringRetry_ThrowsOperationCancelled()
    {
        var handler = new SequentialHandler([
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.OK,
        ]);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var options = new HttpRetryOptions
        {
            MaxRetries = 5,
            BaseDelay = TimeSpan.FromSeconds(10),
            MaxDelay = TimeSpan.FromSeconds(30),
        };

        using var cts = new CancellationTokenSource();
        // Cancel after a very short delay — should cancel during the Task.Delay between retries
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var act = async () =>
            await HttpRetryHelper.PostWithRetryAsync(
                client,
                new { },
                options,
                NullLogger.Instance,
                cts.Token
            );

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region SequentialHandler

    private class SequentialHandler(List<HttpStatusCode> statusCodes) : HttpMessageHandler
    {
        private int _callIndex;
        public int RequestCount => _callIndex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var index = _callIndex < statusCodes.Count ? _callIndex : statusCodes.Count - 1;
            _callIndex++;

            var response = new HttpResponseMessage(statusCodes[index])
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };

            return Task.FromResult(response);
        }
    }

    #endregion
}
