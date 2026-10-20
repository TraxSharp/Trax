using System.Data.Common;
using AwesomeAssertions;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A timer whose fire throws because the database failed (unreachable, out of resources, shut down, timed out, or a
/// transaction rolled back for a deadlock or a serialization failure) is backed off without counting an attempt; one
/// whose fire throws because of the row's data (a constraint, a value it cannot store) counts an attempt, so it is
/// cleared once its attempts run out rather than retried for good. The SQLSTATE class decides; a provider error with
/// none counts as the database's.
/// </summary>
[Property("adr", Adr)]
public class TimerFailureClassificationTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    [TestCase("08006", true, TestName = "a lost connection is the database's")]
    [TestCase("40001", true, TestName = "a serialization failure is the database's")]
    [TestCase("40P01", true, TestName = "a deadlock is the database's")]
    [TestCase("53300", true, TestName = "too many connections is the database's")]
    [TestCase("57014", true, TestName = "a statement timeout is the database's")]
    [TestCase("57P01", true, TestName = "an administrator shutdown is the database's")]
    [TestCase("23505", false, TestName = "a unique violation is the row's")]
    [TestCase("23514", false, TestName = "a check violation is the row's")]
    [TestCase("22021", false, TestName = "a value the database cannot store is the row's")]
    [TestCase(null, true, TestName = "a provider error with no SQLSTATE is the database's")]
    public void A_fire_failure_is_the_databases_by_its_SQLSTATE_class(
        string? sqlState,
        bool databases
    ) =>
        InvokeOutcomeReconciler
            .DatabaseFailed(new InvalidOperationException("wrapped", new StateException(sqlState)))
            .Should()
            .Be(databases, $"({Adr})");

    [Test]
    public void A_timeout_is_the_databases() =>
        InvokeOutcomeReconciler.DatabaseFailed(new TimeoutException()).Should().BeTrue();

    [Test]
    public void Any_other_failure_is_the_rows() =>
        InvokeOutcomeReconciler
            .DatabaseFailed(new InvalidOperationException("The run's input could not be stored."))
            .Should()
            .BeFalse();

    private sealed class StateException(string? sqlState) : DbException("A provider error.")
    {
        public override string? SqlState => sqlState;
    }
}
