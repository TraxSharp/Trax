using AwesomeAssertions;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// How long a requeued entry waits after a failed dispatch: five seconds after the first failure,
/// doubling with each one after, and never more than five minutes.
/// </summary>
[TestFixture]
public class DispatchFailureBackoffTests
{
    [TestCase(0, 5)]
    [TestCase(1, 5)]
    [TestCase(2, 10)]
    [TestCase(3, 20)]
    [TestCase(6, 160)]
    public void Backoff_BelowTheCap_DoublesFromTheFirstBackoff(int attempts, int expectedSeconds)
    {
        DispatchFailure.Backoff(attempts).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [TestCase(7)]
    [TestCase(17)]
    [TestCase(int.MaxValue)]
    public void Backoff_PastTheCap_IsTheMaxBackoff(int attempts)
    {
        DispatchFailure
            .Backoff(attempts)
            .Should()
            .Be(
                DispatchFailure.MaxBackoff,
                "a job that keeps failing to dispatch is retried at most every five minutes"
            );
    }
}
