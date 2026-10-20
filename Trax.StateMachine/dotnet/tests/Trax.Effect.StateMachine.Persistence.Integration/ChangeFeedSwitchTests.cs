using System.Globalization;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The 080 trigger reads <c>trax.snapshot_notify</c> in the session that writes, and a host that listens cannot see
/// another host's session, so the only switch the startup check trusts is the database's own
/// (<c>ALTER DATABASE ... SET</c>): a listening host refuses to start when the database turns the notices off,
/// whatever its own session reads, and when the trigger fires only for replication sessions. A host whose session
/// reads otherwise than the database is warned. Each test has a database of its own, since what it changes is
/// database-wide.
/// </summary>
[Property("adr", Adr)]
public class ChangeFeedSwitchTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "switch-user";

    private ScratchDatabase _db = null!;

    [SetUp]
    public async Task SetUp() => _db = await ScratchDatabase.Create();

    [TearDown]
    public async Task TearDown() => await _db.DisposeAsync();

    private InstanceHost Host(
        StateMachineChangeFeed feed,
        string? sessionSetting = null,
        PostgresChangeFeedTests.RecordingLogger<ChangeFeedStartupValidator>? logger = null
    ) =>
        InstanceHost.Create(
            StoreProvider.Postgres,
            options: o => o.ChangeFeed = feed,
            services: logger is null
                ? null
                : s => s.AddSingleton<ILogger<ChangeFeedStartupValidator>>(logger),
            postgresConnectionString: sessionSetting is null
                ? _db.ConnectionString
                : $"{_db.ConnectionString};Options=-c trax.snapshot_notify={sessionSetting}"
        );

    private static ChangeFeedStartupValidator Validator(InstanceHost host) =>
        host.Services.GetServices<IHostedService>().OfType<ChangeFeedStartupValidator>().Single();

    [Test]
    public async Task A_listening_host_refuses_a_trigger_that_fires_only_for_replication_sessions()
    {
        await _db.Execute(
            "ALTER TABLE trax.snapshot_draft ENABLE REPLICA TRIGGER snapshot_draft_changed_notify"
        );
        using var host = Host(StateMachineChangeFeed.Notify);

        var act = () => Validator(host).StartingAsync(CancellationToken.None);

        (
            await act.Should()
                .ThrowAsync<InvalidOperationException>(
                    $"a trigger enabled for replicas only sends nothing for an ordinary write. See {Adr}"
                )
        ).WithMessage("*ChangeFeed is Notify*snapshot_draft_changed_notify*");
    }

    [Test]
    public async Task A_listening_host_refuses_a_database_that_turns_the_notices_off_whatever_its_own_session_reads()
    {
        await _db.Execute($"ALTER DATABASE {_db.Name} SET trax.snapshot_notify = 'off'");
        using var host = Host(StateMachineChangeFeed.Notify, sessionSetting: "on");

        var act = () => Validator(host).StartingAsync(CancellationToken.None);

        (
            await act.Should()
                .ThrowAsync<InvalidOperationException>(
                    $"every other host's session starts with the database's 'off' and writes without a notice. See {Adr}"
                )
        ).WithMessage("*ChangeFeed is Notify*trax.snapshot_notify*");
    }

    [Test]
    public async Task The_switch_turns_the_notices_off_in_any_case()
    {
        await _db.Execute($"ALTER DATABASE {_db.Name} SET trax.snapshot_notify = 'OFF'");
        using var host = Host(StateMachineChangeFeed.Notify);
        await using var notices = await SnapshotNotices.Listen(_db.ConnectionString);

        var id = Guid.NewGuid();
        using (var scope = host.Scope())
            (await host.Service(scope, "turnstile").Autosave(User, id, TestTurnstile.InitialJson))
                .Should()
                .BeOfType<AutosaveResult.Saved>();
        var rowId = await host.RowId(id);

        (await notices.Collect())
            .Should()
            .NotContain(
                rowId.ToString(CultureInfo.InvariantCulture),
                $"'OFF' is 'off': the trigger compares the setting case-insensitively. See {Adr}"
            );
        var act = () => Validator(host).StartingAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task A_polling_host_whose_session_turns_the_notices_back_on_is_warned()
    {
        await _db.Execute($"ALTER DATABASE {_db.Name} SET trax.snapshot_notify = 'off'");
        var logger = new PostgresChangeFeedTests.RecordingLogger<ChangeFeedStartupValidator>();
        using var host = Host(StateMachineChangeFeed.Poll, sessionSetting: "on", logger: logger);

        await Validator(host).StartingAsync(CancellationToken.None);

        logger
            .Warnings.Should()
            .ContainSingle(
                $"the host's own writes still notify, and only the database's switch is supported. See {Adr}"
            )
            .Which.Should()
            .Contain("session")
            .And.Contain("database");
    }

    [Test]
    public async Task A_polling_host_on_a_database_with_the_notices_off_starts_without_a_warning()
    {
        await _db.Execute($"ALTER DATABASE {_db.Name} SET trax.snapshot_notify = 'off'");
        var logger = new PostgresChangeFeedTests.RecordingLogger<ChangeFeedStartupValidator>();
        using var host = Host(StateMachineChangeFeed.Poll, logger: logger);

        await Validator(host).StartingAsync(CancellationToken.None);

        logger.Warnings.Should().BeEmpty();
    }
}
