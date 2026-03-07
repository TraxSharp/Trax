using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.SchedulerStartupService;

namespace Trax.Scheduler.Tests;

[TestFixture]
public class SchedulerStartupServiceTests
{
    #region Helpers

    private static SchedulerStartupService CreateService()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Trace));

        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<SchedulerStartupService>>();
        var config = new SchedulerConfiguration { RecoverStuckJobsOnStartup = false };

        return new SchedulerStartupService(sp, config, logger);
    }

    #endregion

    #region IsTransient detection

    [Test]
    public void IsTransient_TimeoutException_ReturnsTrue()
    {
        var ex = new TimeoutException("timed out");
        SchedulerStartupService.IsTransient(ex).Should().BeTrue();
    }

    [Test]
    public void IsTransient_NpgsqlException_ReturnsTrue()
    {
        var ex = new NpgsqlException("connection failed");
        SchedulerStartupService.IsTransient(ex).Should().BeTrue();
    }

    [Test]
    public void IsTransient_InvalidOperationWrappingNpgsqlException_ReturnsTrue()
    {
        var inner = new NpgsqlException("connection failed");
        var ex = new InvalidOperationException("transient failure", inner);
        SchedulerStartupService.IsTransient(ex).Should().BeTrue();
    }

    [Test]
    public void IsTransient_InvalidOperationWrappingTimeout_ReturnsTrue()
    {
        var inner = new TimeoutException("read timeout");
        var ex = new InvalidOperationException("transient failure", inner);
        SchedulerStartupService.IsTransient(ex).Should().BeTrue();
    }

    [Test]
    public void IsTransient_DeepNestedTransient_ReturnsTrue()
    {
        // InvalidOperationException -> InvalidOperationException -> TimeoutException
        var timeout = new TimeoutException("deep timeout");
        var mid = new InvalidOperationException("mid layer", timeout);
        var outer = new InvalidOperationException("outer layer", mid);
        SchedulerStartupService.IsTransient(outer).Should().BeTrue();
    }

    [Test]
    public void IsTransient_ExactProductionStackTrace_ReturnsTrue()
    {
        // Reproduce: InvalidOperationException -> NpgsqlException -> TimeoutException
        var timeout = new TimeoutException("Timeout during reading attempt");
        var npgsql = new NpgsqlException("Exception while reading from stream", timeout);
        var outer = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.",
            npgsql
        );
        SchedulerStartupService.IsTransient(outer).Should().BeTrue();
    }

    [Test]
    public void IsTransient_ArgumentException_ReturnsFalse()
    {
        var ex = new ArgumentException("bad argument");
        SchedulerStartupService.IsTransient(ex).Should().BeFalse();
    }

    [Test]
    public void IsTransient_InvalidOperationWithNonTransientInner_ReturnsFalse()
    {
        var inner = new ArgumentException("not transient");
        var ex = new InvalidOperationException("wrapping", inner);
        SchedulerStartupService.IsTransient(ex).Should().BeFalse();
    }

    [Test]
    public void IsTransient_InvalidOperationWithNoInner_ReturnsFalse()
    {
        var ex = new InvalidOperationException("standalone");
        SchedulerStartupService.IsTransient(ex).Should().BeFalse();
    }

    [Test]
    public void IsTransient_NullReferenceException_ReturnsFalse()
    {
        var ex = new NullReferenceException("oops");
        SchedulerStartupService.IsTransient(ex).Should().BeFalse();
    }

    [Test]
    public void IsTransient_IOException_ReturnsFalse()
    {
        var ex = new IOException("disk error");
        SchedulerStartupService.IsTransient(ex).Should().BeFalse();
    }

    [Test]
    public void IsTransient_InvalidOperationWrappingIOException_ReturnsFalse()
    {
        var inner = new IOException("disk error");
        var ex = new InvalidOperationException("wrapping", inner);
        SchedulerStartupService.IsTransient(ex).Should().BeFalse();
    }

    #endregion

    #region SeedWithRetryAsync — success scenarios

    [Test]
    public async Task SeedWithRetryAsync_SucceedsOnFirstAttempt_CallsActionOnce()
    {
        var callCount = 0;
        var service = CreateService();

        await service.SeedWithRetryAsync(
            _ =>
            {
                callCount++;
                return Task.CompletedTask;
            },
            "test-1",
            CancellationToken.None,
            baseDelay: TimeSpan.Zero
        );

        callCount.Should().Be(1);
    }

    [Test]
    public async Task SeedWithRetryAsync_TransientThenSuccess_RetriesAndSucceeds()
    {
        var callCount = 0;
        var service = CreateService();

        await service.SeedWithRetryAsync(
            _ =>
            {
                callCount++;
                if (callCount <= 2)
                    throw new InvalidOperationException(
                        "transient",
                        new TimeoutException("read timeout")
                    );
                return Task.CompletedTask;
            },
            "retry-test",
            CancellationToken.None,
            baseDelay: TimeSpan.Zero
        );

        callCount.Should().Be(3);
    }

    [Test]
    public async Task SeedWithRetryAsync_NpgsqlExceptionThenSuccess_RetriesAndSucceeds()
    {
        var callCount = 0;
        var service = CreateService();

        await service.SeedWithRetryAsync(
            _ =>
            {
                callCount++;
                if (callCount == 1)
                    throw new NpgsqlException("connection reset");
                return Task.CompletedTask;
            },
            "npgsql-retry",
            CancellationToken.None,
            baseDelay: TimeSpan.Zero
        );

        callCount.Should().Be(2);
    }

    [Test]
    public async Task SeedWithRetryAsync_TimeoutExceptionThenSuccess_RetriesAndSucceeds()
    {
        var callCount = 0;
        var service = CreateService();

        await service.SeedWithRetryAsync(
            _ =>
            {
                callCount++;
                if (callCount == 1)
                    throw new TimeoutException("timed out");
                return Task.CompletedTask;
            },
            "timeout-retry",
            CancellationToken.None,
            baseDelay: TimeSpan.Zero
        );

        callCount.Should().Be(2);
    }

    [Test]
    public async Task SeedWithRetryAsync_ExactProductionException_RetriesAndSucceeds()
    {
        // Reproduce the exact exception nesting from the production failure:
        // InvalidOperationException -> NpgsqlException -> TimeoutException
        var callCount = 0;
        var service = CreateService();

        await service.SeedWithRetryAsync(
            _ =>
            {
                callCount++;
                if (callCount == 1)
                {
                    var timeout = new TimeoutException("Timeout during reading attempt");
                    var npgsql = new NpgsqlException(
                        "Exception while reading from stream",
                        timeout
                    );
                    throw new InvalidOperationException(
                        "An exception has been raised that is likely due to a transient failure.",
                        npgsql
                    );
                }
                return Task.CompletedTask;
            },
            "prod-repro",
            CancellationToken.None,
            baseDelay: TimeSpan.Zero
        );

        callCount.Should().Be(2, "the exact production exception should be retried");
    }

    [Test]
    public async Task SeedWithRetryAsync_FailsOnAttempts1Through4_SucceedsOnAttempt5()
    {
        var callCount = 0;
        var service = CreateService();

        await service.SeedWithRetryAsync(
            _ =>
            {
                callCount++;
                if (callCount < 5)
                    throw new TimeoutException("still timing out");
                return Task.CompletedTask;
            },
            "edge-case",
            CancellationToken.None,
            baseDelay: TimeSpan.Zero
        );

        callCount.Should().Be(5, "should succeed on the 5th and final attempt");
    }

    #endregion

    #region SeedWithRetryAsync — failure scenarios

    [Test]
    public async Task SeedWithRetryAsync_NonTransientFailure_ThrowsImmediately()
    {
        var callCount = 0;
        var service = CreateService();

        var act = () =>
            service.SeedWithRetryAsync(
                _ =>
                {
                    callCount++;
                    throw new ArgumentException("bad config");
                },
                "fatal-test",
                CancellationToken.None,
                baseDelay: TimeSpan.Zero
            );

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("bad config");
        callCount.Should().Be(1, "non-transient errors should not be retried");
    }

    [Test]
    public async Task SeedWithRetryAsync_AllRetriesExhausted_ThrowsLastException()
    {
        var callCount = 0;
        var service = CreateService();

        var act = () =>
            service.SeedWithRetryAsync(
                _ =>
                {
                    callCount++;
                    throw new InvalidOperationException(
                        "transient",
                        new TimeoutException("always times out")
                    );
                },
                "exhaust-test",
                CancellationToken.None,
                baseDelay: TimeSpan.Zero
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("transient");
        callCount.Should().Be(5, "should exhaust all 5 retry attempts");
    }

    [Test]
    public async Task SeedWithRetryAsync_AllRetriesExhausted_ThrowsNpgsqlOnFinalAttempt()
    {
        var callCount = 0;
        var service = CreateService();

        var act = () =>
            service.SeedWithRetryAsync(
                _ =>
                {
                    callCount++;
                    throw new NpgsqlException("connection refused");
                },
                "npgsql-exhaust",
                CancellationToken.None,
                baseDelay: TimeSpan.Zero
            );

        await act.Should().ThrowAsync<NpgsqlException>();
        callCount.Should().Be(5);
    }

    #endregion

    #region SeedWithRetryAsync — cancellation

    [Test]
    public async Task SeedWithRetryAsync_CancellationDuringRetryDelay_ThrowsCancellation()
    {
        var callCount = 0;
        using var cts = new CancellationTokenSource();
        var service = CreateService();

        var act = () =>
            service.SeedWithRetryAsync(
                _ =>
                {
                    callCount++;
                    // Cancel after the first transient failure, so the Task.Delay throws
                    cts.Cancel();
                    throw new TimeoutException("timed out");
                },
                "cancel-test",
                cts.Token,
                baseDelay: TimeSpan.Zero
            );

        await act.Should().ThrowAsync<OperationCanceledException>();
        callCount.Should().Be(1);
    }

    [Test]
    public async Task SeedWithRetryAsync_AlreadyCancelled_ThrowsWithoutCalling()
    {
        var callCount = 0;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = CreateService();

        var act = () =>
            service.SeedWithRetryAsync(
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    callCount++;
                    return Task.CompletedTask;
                },
                "pre-cancelled",
                cts.Token,
                baseDelay: TimeSpan.Zero
            );

        await act.Should().ThrowAsync<OperationCanceledException>();
        callCount.Should().Be(0, "action should check the token and never increment");
    }

    #endregion

    #region SeedWithRetryAsync — mixed transient/non-transient

    [Test]
    public async Task SeedWithRetryAsync_TransientThenNonTransient_StopsOnNonTransient()
    {
        var callCount = 0;
        var service = CreateService();

        var act = () =>
            service.SeedWithRetryAsync(
                _ =>
                {
                    callCount++;
                    if (callCount == 1)
                        throw new TimeoutException("transient");
                    throw new ArgumentException("fatal on second attempt");
                },
                "mixed-test",
                CancellationToken.None,
                baseDelay: TimeSpan.Zero
            );

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("fatal on second attempt");
        callCount.Should().Be(2, "should retry the transient, then stop on non-transient");
    }

    #endregion
}
