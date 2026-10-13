using NUnit.Framework.Interfaces;
using Trax.Effect.Data.Testing;
using Trax.Samples.Recovery.Browser.Factories;
using Trax.Samples.Shared.Testing;

[assembly: Trax.Samples.Recovery.Browser.Fixtures.CheckTraxInvariants]

namespace Trax.Samples.Recovery.Browser.Fixtures;

/// <summary>
/// Checks the browser tests' database with <see cref="TraxInvariants"/> after every test, once the test and
/// what the page made the host do have finished.
/// </summary>
/// <remarks>
/// The database lives across runs and no test empties it, so the check compares against what was there when
/// the test began and reports only the violations this test added. The host keeps running between tests, so a run
/// found in progress is reported only if it is still in progress after <see cref="BrowserTimeouts.Server"/>. The topic map's <c>Building</c> invokes a
/// train, so its drafts are also checked to hold a run token exactly while they are building.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class CheckTraxInvariantsAttribute : Attribute, ITestAction
{
    private static readonly InvokingState[] InvokingStates = [new("topic-map", "Building")];

    private IReadOnlyList<TraxInvariantViolation> _before = [];

    public ActionTargets Targets => ActionTargets.Test;

    public void BeforeTest(ITest test) => _before = Find();

    public void AfterTest(ITest test)
    {
        if (
            test.Fixture is null
            || TraxInvariants.IsExempt(test.Fixture.GetType(), test.MethodName)
        )
            return;

        // The host is still running when the check runs, and its scheduler records a run of its own every
        // second (the manifest manager), so a run can be caught in progress that finishes a moment later. A run
        // still in progress once the host had time to finish it is stuck, and fails the test like any violation.
        var added = Added();
        if (added.Count > 0 && added.All(v => v.Invariant == TraxInvariants.RunInProgress))
            Polling
                .WaitUntilAsync(
                    () => Task.FromResult((added = Added()).Count == 0),
                    BrowserTimeouts.Server,
                    TimeSpan.FromMilliseconds(250)
                )
                .GetAwaiter()
                .GetResult();
        if (added.Count > 0)
            Assert.Fail(TraxInvariants.Describe(added));
    }

    private List<TraxInvariantViolation> Added() => Find().Except(_before).ToList();

    // ITestAction is synchronous, and the check runs between tests, never inside one.
    private static IReadOnlyList<TraxInvariantViolation> Find() =>
        TraxInvariants
            .FindViolationsAsync(RecoveryBrowserHost.ConnectionString, InvokingStates)
            .GetAwaiter()
            .GetResult();
}
