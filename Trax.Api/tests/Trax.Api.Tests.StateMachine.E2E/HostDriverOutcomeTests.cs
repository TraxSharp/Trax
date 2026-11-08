using AwesomeAssertions;
using Trax.StateMachine.Gherkin.Reqnroll;
using Move = Trax.StateMachine.Gherkin.Reqnroll.HostDriver.Move;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Where the host bindings read an invoked train's outcome from: the instance's history, the first move out of the
/// state after the instance last entered it. A state entered again, after a retry, is not answered by the move out of
/// the visit before.
/// </summary>
[TestFixture]
public class HostDriverOutcomeTests
{
    // Charging failed to Declined, and Retry brought it back to Charging.
    private static readonly Move[] Retried =
    [
        new(1, null, "Draft"),
        new(2, "Draft", "Charging"),
        new(3, "Charging", "Declined"),
        new(4, "Declined", "Charging"),
    ];

    [Test]
    public void A_state_entered_again_has_not_been_left_until_the_new_train_ends()
    {
        var entered = HostDriver.Entered(Retried, "Charging");

        HostDriver.Left(Retried, "Charging", entered).Should().BeNull();
    }

    [Test]
    public void The_move_out_of_the_current_visit_is_where_the_outcome_went()
    {
        var entered = HostDriver.Entered(Retried, "Charging");
        Move[] paid = [.. Retried, new(5, "Charging", "Paid"), new(6, "Paid", "Baking")];

        HostDriver.Left(paid, "Charging", entered).Should().Be("Paid");
    }

    [Test]
    public void The_states_since_the_last_own_move_run_from_it_through_the_policy_moves_after_it()
    {
        Move[] history =
        [
            new(1, null, "Draft"),
            new(2, "Draft", "Placed"),
            new(3, "Placed", "Baking", ByPolicy: true),
        ];

        HostDriver.SinceLastOwnMove(history).Should().Equal("Placed", "Baking");
        HostDriver.SinceLastOwnMove(history[..2]).Should().Equal("Placed");
        HostDriver.SinceLastOwnMove([]).Should().BeEmpty();
    }

    [Test]
    public void A_state_passed_through_is_not_where_the_outcome_went()
    {
        Move[] history = [new(1, null, "Draft"), new(2, "Draft", "Charging")];
        var entered = HostDriver.Entered(history, "Charging");
        Move[] moved = [.. history, new(3, "Charging", "Paid"), new(4, "Paid", "Baking")];

        HostDriver.Left(moved, "Charging", entered).Should().Be("Paid");
    }
}
