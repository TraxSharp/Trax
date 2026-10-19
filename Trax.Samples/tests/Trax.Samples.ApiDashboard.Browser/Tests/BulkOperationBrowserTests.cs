using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Trax.Effect.Enums;
using Trax.Samples.ApiDashboard.Browser.Fixtures;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Samples.ApiDashboard.Browser.Pages;

namespace Trax.Samples.ApiDashboard.Browser.Tests;

/// <summary>
/// An operator cancels every run a filter matches from the runs list's batch bar: the console starts one bulk
/// operation over the list's filter and the count it showed, confirmed by typing that count, and opens the
/// operation's page, which follows it until it finishes (Trax.Api/docs/adr/0045).
/// </summary>
[TestFixture]
public class BulkOperationBrowserTests : ConsoleTest
{
    private static readonly float Render = BrowserTimeouts.Render.Ms();

    [Test]
    [Covers("route:/runs")]
    [Covers("route:/system/operations/:id")]
    [Covers("route:/system/operations")]
    [Covers("mutation:StartBulkOperation")]
    public async Task A_bulk_cancel_over_a_filter_shows_its_operation_with_the_right_counts()
    {
        var held = await Seed.HeldRunsAsync(3);
        var train = typeof(IReconcileAccountTrain).FullName!;
        // An earlier test's runs of this train were released when it ended; wait until they have finished, so
        // the filter matches this test's three.
        await Wait.UntilAsync(
            async () => await ActiveRunsOf(train) == held.Count,
            BrowserTimeouts.Server,
            $"only this test's runs of {train} are in progress"
        );

        var runs = new FilteredRunsPage(
            Console,
            $"state=IN_PROGRESS&train={Uri.EscapeDataString(train)}"
        );
        await runs.OpenAsync(Caller.Operator);
        await runs.Row(held[0].Id)
            .GetByRole(AriaRole.Checkbox)
            .CheckAsync(new() { Timeout = Render });

        var started = Page.WaitForRequestAsync(
            r =>
                r.Url.EndsWith("/trax/graphql")
                && r.PostData?.Contains("\"operationName\":\"StartBulkOperation\"") == true,
            new() { Timeout = Render }
        );
        await runs.CancelAllMatching(held.Count).ClickAsync(new() { Timeout = Render });
        var dialog = Page.GetByRole(
            AriaRole.Dialog,
            new() { Name = "Cancel all matching runs", Exact = true }
        );
        await dialog.GetByLabel($"Type {held.Count} to confirm").FillAsync($"{held.Count}");
        await Console.ConfirmAsync(
            "Cancel all matching runs",
            $"this filter matches: {held.Count}"
        );

        var variables = (await started).PostDataJSON()!.Value.GetProperty("variables");
        variables.GetProperty("kind").GetString().Should().Be("CANCEL");
        variables.GetProperty("expectedCount").GetInt32().Should().Be(held.Count);
        var filter = variables.GetProperty("filter").GetProperty("executions");
        filter.GetProperty("trainName").GetString().Should().Be(train);
        filter.GetProperty("trainState").GetString().Should().Be("IN_PROGRESS");

        var operation = new BulkOperationPage(Console);
        await Assertions.Expect(operation.Heading).ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(Page)
            .ToHaveURLAsync(
                new Regex("/system/operations/[0-9a-f-]{36}$"),
                new() { Timeout = Render }
            );
        // The page reads the operation again while it runs; these hold once it has finished.
        await Assertions
            .Expect(Page.GetByText("Finished", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = BrowserTimeouts.Server.Ms() });
        foreach (
            var (label, count) in new[]
            {
                ("Matched", held.Count),
                ("Done", held.Count),
                ("Failed", 0),
                ("Not attempted", 0),
            }
        )
            await Assertions
                .Expect(operation.Count(label).GetByText($"{count}", new() { Exact = true }))
                .ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions.Expect(operation.Filter).ToContainTextAsync(train);
        await Assertions.Expect(operation.Filter).ToContainTextAsync("In progress");
        // The actor is the caller's principal id, {scheme}:{id}.
        await Assertions
            .Expect(Page.GetByText(new Regex($"^[\\w-]+:{Caller.Operator.Id}$")))
            .ToBeVisibleAsync();

        var ids = held.Select(r => r.Id).ToList();
        (
            await Host.QueryAsync(dc =>
                dc.Metadatas.AsNoTracking()
                    .CountAsync(m => ids.Contains(m.Id) && m.CancellationRequested)
            )
        )
            .Should()
            .Be(held.Count, "every run the filter matched was asked to cancel");

        await Console.NavLink("Bulk operations").ClickAsync();
        var list = new BulkOperationsPage(Console);
        await Assertions.Expect(list.Heading).ToBeVisibleAsync(new() { Timeout = Render });
        await Assertions
            .Expect(list.Operation("Cancel runs").First)
            .ToBeVisibleAsync(new() { Timeout = Render });

        Checks
            .Sent.Count(s => s.OperationName == "StartBulkOperation")
            .Should()
            .Be(1, "one confirmation starts one operation");
        Checks
            .Sent.Count(s => s.OperationName == "CancelExecutions")
            .Should()
            .Be(0, "the selection is not what is cancelled");
    }

    private static Task<int> ActiveRunsOf(string train) =>
        Host.QueryAsync(dc =>
            dc.Metadatas.AsNoTracking()
                .CountAsync(m =>
                    m.Name == train
                    && (m.TrainState == TrainState.InProgress || m.TrainState == TrainState.Pending)
                )
        );
}
