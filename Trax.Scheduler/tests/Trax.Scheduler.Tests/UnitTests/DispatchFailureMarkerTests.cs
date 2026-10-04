using AwesomeAssertions;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// The <c>FailureException</c> a requeued dispatch attempt's run carries is a stored value other
/// code reads, not only this repo's constant.
/// </summary>
[TestFixture]
public class DispatchFailureMarkerTests
{
    [Test]
    public void Requeued_IsTheLiteralOtherReadersMatch()
    {
        // Trax.Effect's run-attempt reader (RunAttempts.RequeuedDispatch) matches the same literal
        // to leave a requeued dispatch attempt out of a run's attempts, and rows already written
        // carry it. Changing it here would silently split the two.
        DispatchFailure.Requeued.Should().Be("DispatchRequeued");
    }
}
