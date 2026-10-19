using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Trax.Effect.Enums;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Samples.ApiDashboard.Browser.Pages;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// The Queued view's actions: an entry still waiting is cancelled from its row, from a selection or from its own
/// page, and Queue train writes a new entry with the input it was given, or says why it did not.
/// </summary>
[TestFixture]
public class QueueBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/runs/queued")]
    [Covers("mutation:CancelWorkQueueEntry")]
    public async Task Cancel_on_a_waiting_row_cancels_that_entry()
    {
        var (blocked, _) = await Seed.BlockedQueueEntryAsync();
        var queue = new WorkQueuePage(Console);
        await queue.OpenAsync(Caller.Operator);
        var row = queue.Row(blocked.Id);
        await Assertions.Expect(row).ToBeVisibleAsync(new() { Timeout = Render });

        var sent = Console.SendsAsync("CancelWorkQueueEntry");
        await queue.Button(row, "Cancel").ClickAsync();
        await Console.ConfirmAsync("Cancel queued entry", $"Cancel queued entry #{blocked.Id}?");
        await sent;

        await Assertions
            .Expect(Console.Toast($"Entry #{blocked.Id} cancelled."))
            .ToBeVisibleAsync(new() { Timeout = Render });
        // A cancelled entry can no longer be selected; its row is found by its id's link.
        await Assertions
            .Expect(Console.RowLinked($"#{blocked.Id}"))
            .ToContainTextAsync("Cancelled", new() { Timeout = Render });
        (await StatusOf(blocked.Id)).Should().Be(WorkQueueStatus.Cancelled);
    }

    [Test]
    [Covers("route:/runs/queued")]
    [Covers("mutation:CancelWorkQueueEntries")]
    public async Task Cancel_selected_cancels_each_selected_entry_in_one_request()
    {
        var (first, _) = await Seed.BlockedQueueEntryAsync();
        var (second, _) = await Seed.BlockedQueueEntryAsync();
        var queue = new WorkQueuePage(Console);
        await queue.OpenAsync(Caller.Operator);
        foreach (var id in new[] { first.Id, second.Id })
            await queue.Row(id).GetByRole(AriaRole.Checkbox).CheckAsync(new() { Timeout = Render });

        var sent = Console.SendsAsync("CancelWorkQueueEntries");
        await Console.Button("Cancel selected").ClickAsync();
        await Console.ConfirmAsync("Cancel queued entries", "Cancel 2 queued entr(ies)?");
        var ids = (await sent).PostDataJSON()!.Value.GetProperty("variables").GetProperty("ids");
        ids.EnumerateArray()
            .Select(i => i.GetInt64())
            .Should()
            .BeEquivalentTo([first.Id, second.Id]);

        await Assertions
            .Expect(Console.RowLinked($"#{first.Id}"))
            .ToContainTextAsync("Cancelled", new() { Timeout = Render });
        (await StatusOf(first.Id)).Should().Be(WorkQueueStatus.Cancelled);
        (await StatusOf(second.Id)).Should().Be(WorkQueueStatus.Cancelled);
        Checks.Sent.Count(s => s.OperationName == "CancelWorkQueueEntries").Should().Be(1);
    }

    [Test]
    [Covers("route:/runs/queued/:id")]
    [Covers("mutation:CancelWorkQueueEntry")]
    public async Task The_entry_page_shows_the_entry_and_cancels_it()
    {
        var (blocked, _) = await Seed.BlockedQueueEntryAsync();
        var entry = new QueueEntryPage(Console, blocked.Id, nameof(IReconcileAccountTrain));
        await entry.OpenAsync(Caller.Operator);
        await Assertions
            .Expect(entry.DetailField("Status"))
            .ToHaveTextAsync("Queued", new() { Timeout = Render });
        await Assertions.Expect(entry.DetailField("Subject")).ToHaveTextAsync(blocked.SubjectKey!);

        var sent = Console.SendsAsync("CancelWorkQueueEntry");
        await Console.Button("Cancel entry").ClickAsync();
        await Console.ConfirmAsync("Cancel queued entry", $"Cancel queued entry #{blocked.Id}?");
        await sent;

        await Assertions
            .Expect(entry.DetailField("Status"))
            .ToHaveTextAsync("Cancelled", new() { Timeout = Render });
        await Assertions.Expect(Console.Button("Cancel entry")).ToHaveCountAsync(0);
        (await StatusOf(blocked.Id)).Should().Be(WorkQueueStatus.Cancelled);
    }

    [Test]
    [Covers("route:/runs/queued")]
    [Covers("mutation:QueueTrain")]
    public async Task Queue_train_queues_the_train_with_the_input_typed_and_opens_its_entry()
    {
        var key = Seed.NewKey("queued");
        var queue = new WorkQueuePage(Console);
        await queue.OpenAsync(Caller.Operator);

        await Console.Button("Queue train").ClickAsync();
        var dialog = Console.Dialog("Queue a train");
        await dialog
            .GetByLabel("Train", new() { Exact = true })
            .SelectOptionAsync(new SelectOptionValue { Label = nameof(IImportFeedTrain) });
        await dialog.GetByLabel("Field key", new() { Exact = true }).FillAsync(key);
        var sent = Console.SendsAsync("QueueTrain");
        await dialog
            .GetByRole(AriaRole.Button, new() { Name = "Queue train", Exact = true })
            .ClickAsync();
        await sent;

        await Assertions
            .Expect(Page)
            .ToHaveURLAsync(new Regex(@"/runs/queued/\d+$"), new() { Timeout = Render });
        var id = long.Parse(Page.Url[(Page.Url.LastIndexOf('/') + 1)..]);
        var stored = await Host.QueryAsync(dc =>
            dc.WorkQueues.AsNoTracking().SingleAsync(w => w.Id == id)
        );
        stored.TrainName.Should().Be(typeof(IImportFeedTrain).FullName);
        stored.Input.Should().Contain(key, "the entry carries the input typed in the dialog");
        await Assertions
            .Expect(new QueueEntryPage(Console, id, nameof(IImportFeedTrain)).Heading)
            .ToBeVisibleAsync(new() { Timeout = Render });
    }

    [Test]
    [Covers("route:/runs/queued")]
    [Covers("mutation:QueueTrain")]
    public async Task Queue_train_keeps_the_dialog_open_and_says_why_when_the_input_is_not_JSON()
    {
        var before = await EntriesOf<IImportFeedTrain>();
        var queue = new WorkQueuePage(Console);
        await queue.OpenAsync(Caller.Operator);

        await Console.Button("Queue train").ClickAsync();
        var dialog = Console.Dialog("Queue a train");
        await dialog
            .GetByLabel("Train", new() { Exact = true })
            .SelectOptionAsync(new SelectOptionValue { Label = nameof(IImportFeedTrain) });
        await dialog.GetByLabel("Input JSON", new() { Exact = true }).FillAsync("{ not json");
        var sent = Console.SendsAsync("QueueTrain");
        await dialog
            .GetByRole(AriaRole.Button, new() { Name = "Queue train", Exact = true })
            .ClickAsync();
        await sent;

        await Assertions
            .Expect(dialog.GetByRole(AriaRole.Alert))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/runs/queued$"));
        (await EntriesOf<IImportFeedTrain>()).Should().Be(before, "nothing was queued");
    }

    private static Task<WorkQueueStatus> StatusOf(long id) =>
        Host.QueryAsync(dc =>
            dc.WorkQueues.AsNoTracking().Where(w => w.Id == id).Select(w => w.Status).SingleAsync()
        );

    private static Task<int> EntriesOf<TTrain>() =>
        Host.QueryAsync(dc =>
            dc.WorkQueues.AsNoTracking().CountAsync(w => w.TrainName == typeof(TTrain).FullName)
        );
}
