using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>Runs: every run the host has recorded.</summary>
public sealed class RunsPage(ConsolePage console) : AreaPage(console, "runs", "Runs")
{
    /// <summary>The Failed view: All runs filtered to state FAILED.</summary>
    public ILocator FailedHeading => Console.Heading("Failed runs");

    public ILocator ExternalIdFilter => Page.GetByLabel("External id", new() { Exact = true });

    /// <summary>
    /// The row of one run, found by filtering the list to its external id; a run that has ended has no checkbox
    /// to find it by. <paramref name="train"/> is the name its row links, the train's short name.
    /// </summary>
    public async Task<ILocator> FindAsync(string externalId, string train)
    {
        await ExternalIdFilter.FillAsync(externalId).ConfigureAwait(false);
        var row = Console.RowLinked(train);
        // The list reloads once the filter settles; then only this run's row is left.
        await Assertions
            .Expect(
                Page.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Link) })
            )
            .ToHaveCountAsync(1, new() { Timeout = BrowserTimeouts.Render.Ms() })
            .ConfigureAwait(false);
        return row;
    }
}
