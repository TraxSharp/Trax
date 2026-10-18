using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Trax.Effect.Enums;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Samples.ApiDashboard.Browser.Pages;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// The harness the console's browser tests are written in: each seed builder reaches the state it names, on the
/// host and on the page that shows it today, and each caller is let in or refused as it should be. A page item
/// that changes how a state is shown changes the assertion here with its page object.
/// </summary>
[TestFixture]
public class HarnessBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    public async Task Two_dead_letters_that_failed_alike_are_listed_with_one_reason()
    {
        var (first, second) = await Seed.DeadLettersSharingAReasonAsync();
        var page = new DeadLettersPage(Console);

        await page.OpenAsync(Caller.Operator);
        await page.ShowAwaitingAsync();

        foreach (var deadLetter in new[] { first, second })
            await Assertions
                .Expect(page.Row(deadLetter.Id))
                .ToContainTextAsync(first.Reason, new() { Timeout = Render });
    }

    [Test]
    public async Task A_failed_run_with_a_checkpoint_and_a_run_a_machine_invoked_are_listed()
    {
        var failed = await Seed.FailedRunWithCheckpointAsync();
        var gated = await Seed.GatedRunAsync();
        var (instance, invoked) = await Seed.RunInvokedByMachineAsync();

        invoked.InvokingMachine.Should().Be(RelayMachine.Id);
        invoked.InvokingInstanceId.Should().Be(instance.Id);
        gated.TrainState.Should().Be(TrainState.Completed);

        var runs = new RunsPage(Console);
        await runs.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(await runs.FindAsync(failed.ExternalId, nameof(ISettleClaimTrain)))
            .ToContainTextAsync("Failed", new() { Timeout = Render });
        await Assertions
            .Expect(await runs.FindAsync(gated.ExternalId, nameof(ISettleClaimTrain)))
            .ToContainTextAsync("Completed", new() { Timeout = Render });
        await Assertions
            .Expect(await runs.FindAsync(invoked.ExternalId, nameof(IRelayParcelTrain)))
            .ToContainTextAsync("Completed", new() { Timeout = Render });
    }

    [Test]
    public async Task A_staged_and_a_blocked_entry_wait_in_the_queue()
    {
        var staged = await Seed.StagedQueueEntryAsync();
        var (blocked, holding) = await Seed.BlockedQueueEntryAsync();

        holding.TrainState.Should().Be(TrainState.InProgress);
        blocked.SubjectKey.Should().NotBeNull();

        var queue = new WorkQueuePage(Console);
        await queue.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(queue.Row(staged.Id))
            .ToContainTextAsync("Staged", new() { Timeout = Render });
        await Assertions
            .Expect(queue.Row(blocked.Id))
            .ToContainTextAsync(blocked.SubjectKey!, new() { Timeout = Render });
    }

    [Test]
    public async Task An_overdue_timer_and_an_instance_waiting_on_an_ended_run_stay_where_they_are()
    {
        var overdue = await Seed.OverdueTimerAsync();
        var (waiting, entry) = await Seed.InstanceWaitingOnAnEndedRunAsync();

        entry.Status.Should().Be(WorkQueueStatus.Cancelled);
        (await StateOf(overdue.Id))
            .Should()
            .Be(nameof(ReminderState.Pending), "its timer has not fired");
        (await StateOf(waiting.Id))
            .Should()
            .Be(nameof(RelayState.Relaying), "the outcome of its ended run is not applied");

        var machines = new MachinesPage(Console);
        await machines.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(machines.CountsByState)
            .ToContainTextAsync(ReminderMachine.Id, new() { Timeout = Render });
        await Assertions
            .Expect(machines.CountsByState)
            .ToContainTextAsync(RelayMachine.Id, new() { Timeout = Render });
    }

    [Test]
    public async Task A_disabled_manifest_and_a_paused_dispatcher_are_shown()
    {
        var disabled = await Seed.DisabledManifestAsync();
        await Seed.PausedDispatcherAsync();
        disabled.IsEnabled.Should().BeFalse();

        var manifests = new ManifestsPage(Console);
        await manifests.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(manifests.Row(disabled.Id))
            .ToContainTextAsync("Disabled", new() { Timeout = Render });

        var settings = new SchedulerSettingsPage(Console);
        await Page.GotoAsync(Console.Url(settings.Route));
        await Console.ConnectAsync(Caller.Operator);
        await Assertions.Expect(settings.Heading).ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(settings.DispatcherEnabled)
            .Not.ToBeCheckedAsync(new() { Timeout = Render });
    }

    [Test]
    public async Task A_persisted_operation_is_listed_for_its_managers_only()
    {
        var operation = await Seed.PersistedOperationAsync();
        var page = new PersistedOperationsPage(Console);

        await page.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(page.Operation(operation.Id))
            .ToBeVisibleAsync(new() { Timeout = Render });

        await Page.GotoAsync(Console.Url(page.Route));
        await Console.ConnectAsync(Caller.NoManagement);
        await Assertions.Expect(page.NotServed).ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions.Expect(page.Operation(operation.Id)).ToHaveCountAsync(0);
    }

    [Test]
    public async Task A_caller_the_operations_gate_refuses_is_shown_the_refusal()
    {
        var deadLetter = await Seed.DeadLetterAsync();
        var page = new DeadLettersPage(Console);

        await page.OpenAsync(Caller.Refused);
        await Assertions.Expect(page.Refusal).ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions.Expect(page.Row(deadLetter.Id)).ToHaveCountAsync(0);
    }

    private static Task<string> StateOf(Guid instance) =>
        Host.QueryAsync(dc =>
            dc.SnapshotDrafts.AsNoTracking()
                .Where(d => d.Id == instance)
                .Select(d => d.State)
                .SingleAsync()
        );
}
