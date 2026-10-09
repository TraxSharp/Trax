using NUnit.Framework.Interfaces;
using Trax.Effect.Data.Testing;

[assembly: Trax.Mediator.Tests.StateMachine.Integration.Fixtures.CheckTraxInvariants]

namespace Trax.Mediator.Tests.StateMachine.Integration.Fixtures;

/// <summary>
/// Checks the throwaway database with <see cref="TraxInvariants"/> after every test in this
/// assembly, once the test and everything it started have finished.
/// </summary>
/// <remarks>
/// The database lives for the whole run (<see cref="PostgresSetup"/>) and no test empties it, so
/// the check compares against what was already there when the test began and reports only the
/// violations this test added. A test that abandons a claim on purpose carries
/// <see cref="LeavesStuckRunsAttribute"/>; what it leaves behind is then part of every later
/// test's starting point, not a violation of theirs.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class CheckTraxInvariantsAttribute : Attribute, ITestAction
{
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

        var added = Find().Except(_before).ToList();
        if (added.Count > 0)
            Assert.Fail(TraxInvariants.Describe(added));
    }

    // ITestAction is synchronous, and the check runs between tests, never inside one.
    private static IReadOnlyList<TraxInvariantViolation> Find() =>
        TraxInvariants.FindViolationsAsync(PostgresSetup.ConnectionString).GetAwaiter().GetResult();
}
