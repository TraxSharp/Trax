using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.SchedulerStartupService;

namespace Trax.Scheduler.Tests.UnitTests;

[TestFixture]
public class SchedulerStartupServiceTests
{
    #region Helpers

    private static SchedulerStartupService CreateService(ISqlDialect? dialect = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton(dialect ?? new TimeoutIsTransientDialect());

        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<SchedulerStartupService>>();
        var config = new SchedulerConfiguration { RecoverStuckJobsOnStartup = false };

        return new SchedulerStartupService(sp, config, logger);
    }

    /// <summary>
    /// A dialect that calls a failure transient when a <see cref="TimeoutException"/> is anywhere
    /// in its chain, as both shipped dialects do. The real classifications are pinned against
    /// each provider's own dialect in the Postgres and Sqlite integration suites.
    /// </summary>
    private sealed class TimeoutIsTransientDialect : ISqlDialect
    {
        public List<Exception> Asked { get; } = [];

        public bool IsTransient(Exception exception)
        {
            Asked.Add(exception);
            for (Exception? e = exception; e is not null; e = e.InnerException)
                if (e is TimeoutException)
                    return true;
            return false;
        }

        public FormattableString TryAcquireLeaderLock(string lockName) =>
            throw new NotSupportedException();

        public string ClaimWorkQueueEntry() => throw new NotSupportedException();

        public string DequeueBackgroundJobs() => throw new NotSupportedException();

        public string LoadGroupFairQueuedJobs() => throw new NotSupportedException();
    }

    #endregion

    #region IsTransient detection

    [Test]
    public void IsTransient_AsksTheProvidersDialect()
    {
        var dialect = new TimeoutIsTransientDialect();
        var service = CreateService(dialect);
        var failure = new InvalidOperationException("wrapping", new TimeoutException());

        service.IsTransient(failure).Should().BeTrue();
        dialect.Asked.Should().ContainSingle().Which.Should().BeSameAs(failure);
    }

    [Test]
    public void IsTransient_ANonTransientFailureTheDialectRejects_ReturnsFalse()
    {
        CreateService().IsTransient(new ArgumentException("bad argument")).Should().BeFalse();
    }

    [Test]
    public void IsTransient_AnyExceptionFromTheNpgsqlNamespace_IsNotTransientByNameAlone()
    {
        // A Postgres error the dialect does not call transient (a constraint violation, a syntax
        // error) fails the same way on every try, whatever namespace its type lives in.
        CreateService().IsTransient(new NpgsqlException("syntax error")).Should().BeFalse();
    }

    [Test]
    public void IsTransient_WithNoDialectRegistered_ReturnsFalse()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var service = new SchedulerStartupService(
            services,
            new SchedulerConfiguration { RecoverStuckJobsOnStartup = false },
            services.GetRequiredService<ILogger<SchedulerStartupService>>()
        );

        service.IsTransient(new TimeoutException()).Should().BeFalse();
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
