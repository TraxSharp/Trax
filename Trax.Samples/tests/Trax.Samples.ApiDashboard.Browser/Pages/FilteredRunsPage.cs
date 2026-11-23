using System.Globalization;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>The runs list filtered through its URL, as a link to a filtered view opens it.</summary>
public sealed class FilteredRunsPage(ConsolePage console, string query)
    : AreaPage(console, $"runs?{query}", "Runs")
{
    /// <summary>The batch bar's button that cancels every run the filter matches.</summary>
    public ILocator CancelAllMatching(int count) =>
        Page.GetByRole(
            AriaRole.Button,
            new()
            {
                Name = string.Create(CultureInfo.InvariantCulture, $"Cancel all {count} matching"),
                Exact = true,
            }
        );
}
