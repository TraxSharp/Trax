using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// The Trains list names every train the host registers and opens each one's page, and its Run and Queue start
/// one with the input typed, or say why they did not.
/// </summary>
[TestFixture]
public class TrainActionsBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/trains")]
    public async Task The_trains_list_names_each_registered_train_and_opens_its_page()
    {
        await OpenTrainsAsync();
        foreach (
            var train in new[]
            {
                typeof(IAuditLedgerTrain),
                typeof(ISettleClaimTrain),
                typeof(IRelayParcelTrain),
            }
        )
            await Assertions
                .Expect(Page.GetByRole(AriaRole.Link, new() { Name = train.Name, Exact = true }))
                .ToBeVisibleAsync(new() { Timeout = Render });

        // The train that never ran stays so: other tests read its empty state.
        await Page.GetByRole(
                AriaRole.Link,
                new() { Name = nameof(IAuditLedgerTrain), Exact = true }
            )
            .ClickAsync();
        await Assertions
            .Expect(Console.Heading(nameof(IAuditLedgerTrain)))
            .ToBeVisibleAsync(new() { Timeout = Render });
        Page.Url.Should().EndWith($"/trains/{typeof(IAuditLedgerTrain).FullName}");
    }

    [Test]
    [Covers("route:/trains")]
    [Covers("mutation:RunTrain")]
    public async Task Run_runs_the_train_now_with_the_input_typed_and_opens_the_run()
    {
        var key = Seed.NewKey("feed");
        await OpenTrainsAsync();
        await Console
            .Button($"Run {nameof(IImportFeedTrain)}")
            .ClickAsync(new() { Timeout = Render });
        var dialog = Console.Dialog("Run a train");
        await Assertions
            .Expect(dialog.GetByLabel("Train", new() { Exact = true }))
            .ToHaveValueAsync(typeof(IImportFeedTrain).FullName!, new() { Timeout = Render });
        await dialog.GetByLabel("Field key", new() { Exact = true }).FillAsync(key);

        var sent = Console.SendsAsync("RunTrain");
        await dialog
            .GetByRole(AriaRole.Button, new() { Name = "Run train", Exact = true })
            .ClickAsync();
        await sent;

        await Assertions
            .Expect(Page)
            .ToHaveURLAsync(new Regex(@"/runs/\d+$"), new() { Timeout = Render });
        var id = long.Parse(Page.Url[(Page.Url.LastIndexOf('/') + 1)..]);
        // The run's input is stored with the run once it has ended.
        var run = await Wait.ForAsync(
            () =>
                Host.QueryAsync(dc =>
                    dc.Metadatas.AsNoTracking()
                        .FirstOrDefaultAsync(m => m.Id == id && m.Input != null)
                ),
            BrowserTimeouts.Server,
            $"run {id} stores its input"
        );
        run.Name.Should().Be(typeof(IImportFeedTrain).FullName);
        run.Input.Should().Contain(key);
        (
            await Host.QueryAsync(dc =>
                dc.WorkQueues.AsNoTracking().AnyAsync(w => w.MetadataId == id)
            )
        )
            .Should()
            .BeFalse("a run started now has no queue entry");
    }

    [Test]
    [Covers("route:/trains")]
    [Covers("mutation:RunTrain")]
    public async Task Run_keeps_the_dialog_open_and_says_why_when_the_input_is_not_JSON()
    {
        await OpenTrainsAsync();
        await Console
            .Button($"Run {nameof(IImportFeedTrain)}")
            .ClickAsync(new() { Timeout = Render });
        var dialog = Console.Dialog("Run a train");
        await dialog.GetByLabel("Input JSON", new() { Exact = true }).FillAsync("{ not json");

        var sent = Console.SendsAsync("RunTrain");
        await dialog
            .GetByRole(AriaRole.Button, new() { Name = "Run train", Exact = true })
            .ClickAsync();
        await sent;

        await Assertions
            .Expect(dialog.GetByRole(AriaRole.Alert))
            .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        Page.Url.Should().EndWith("/trains", "no run was opened");
    }

    [Test]
    [Covers("route:/trains")]
    [Covers("mutation:QueueTrain")]
    public async Task Queue_on_a_trains_row_queues_it_and_opens_the_entry()
    {
        var key = Seed.NewKey("feed");
        await OpenTrainsAsync();
        await Console
            .Button($"Queue {nameof(IImportFeedTrain)}")
            .ClickAsync(new() { Timeout = Render });
        var dialog = Console.Dialog("Queue a train");
        await dialog.GetByLabel("Field key", new() { Exact = true }).FillAsync(key);
        await dialog.GetByLabel("Priority", new() { Exact = true }).FillAsync("3");

        var sent = Console.SendsAsync("QueueTrain");
        await dialog
            .GetByRole(AriaRole.Button, new() { Name = "Queue train", Exact = true })
            .ClickAsync();
        await sent;

        await Assertions
            .Expect(Page)
            .ToHaveURLAsync(new Regex(@"/runs/queued/\d+$"), new() { Timeout = Render });
        var id = long.Parse(Page.Url[(Page.Url.LastIndexOf('/') + 1)..]);
        var entry = await Host.QueryAsync(dc =>
            dc.WorkQueues.AsNoTracking().SingleAsync(w => w.Id == id)
        );
        entry.TrainName.Should().Be(typeof(IImportFeedTrain).FullName);
        entry.Input.Should().Contain(key);
        entry.Priority.Should().Be(3);
    }

    private async Task OpenTrainsAsync()
    {
        await Console.OpenAsync("trains", Caller.Operator);
        await Assertions
            .Expect(Console.Heading("Trains"))
            .ToBeVisibleAsync(new() { Timeout = Render });
    }
}
