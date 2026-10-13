using AwesomeAssertions;
using Trax.Effect.Data.Services.SnapshotNotices;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The change feed's startup check over a given switch, for the cases a shared test database cannot set up without
/// changing a role: a role that turns the notices off for its own sessions writes without one, so a listening host
/// refuses to start, and a value that is neither <c>on</c> nor <c>off</c> keeps the notices on and is warned of.
/// </summary>
[Property("adr", Adr)]
public class ChangeFeedStartupValidatorTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static readonly SnapshotNoticeSwitch Notifying = new(
        TriggerState: "O",
        TriggerHasCondition: true,
        DatabaseValue: null,
        SessionValue: null,
        SessionIsReplica: false,
        RolesTurningOff: []
    );

    private static ChangeFeedStartupValidator Validator(
        StateMachineChangeFeed feed,
        SnapshotNoticeSwitch read,
        PostgresChangeFeedTests.RecordingLogger<ChangeFeedStartupValidator>? logger = null
    ) => new(new StateMachineOptions { ChangeFeed = feed }, new FixedSwitch(read), logger);

    [Test]
    public async Task A_listening_host_refuses_a_role_that_turns_the_notices_off()
    {
        var act = () =>
            Validator(
                    StateMachineChangeFeed.Notify,
                    Notifying with
                    {
                        RolesTurningOff = ["trax_worker"],
                    }
                )
                .StartingAsync(CancellationToken.None);

        (
            await act.Should()
                .ThrowAsync<InvalidOperationException>(
                    $"a writer under that role sends nothing, and this host cannot see its session. See {Adr}"
                )
        ).WithMessage("*ChangeFeed is Notify*trax_worker*");
    }

    [Test]
    public async Task A_listening_host_whose_own_session_is_a_replica_refuses_to_start()
    {
        var act = () =>
            Validator(StateMachineChangeFeed.Notify, Notifying with { SessionIsReplica = true })
                .StartingAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task A_listening_host_on_a_trigger_that_always_fires_starts()
    {
        var logger = new PostgresChangeFeedTests.RecordingLogger<ChangeFeedStartupValidator>();

        await Validator(
                StateMachineChangeFeed.Notify,
                Notifying with
                {
                    TriggerState = "A",
                },
                logger
            )
            .StartingAsync(CancellationToken.None);

        logger.Warnings.Should().BeEmpty();
    }

    [Test]
    public async Task A_value_that_is_neither_on_nor_off_is_warned_of()
    {
        var logger = new PostgresChangeFeedTests.RecordingLogger<ChangeFeedStartupValidator>();

        await Validator(
                StateMachineChangeFeed.Notify,
                Notifying with
                {
                    DatabaseValue = "false",
                    SessionValue = "false",
                },
                logger
            )
            .StartingAsync(CancellationToken.None);

        logger
            .Warnings.Should()
            .ContainSingle(
                $"'false' keeps the notices on, which its writer did not mean. See {Adr}"
            )
            .Which.Should()
            .Contain("'false'");
    }

    private sealed class FixedSwitch(SnapshotNoticeSwitch read) : ISnapshotNoticeSource
    {
        public Task<ISnapshotNoticeSubscription> SubscribeAsync(
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<SnapshotNoticeSwitch> ReadSwitchAsync(CancellationToken cancellationToken) =>
            Task.FromResult(read);
    }
}
