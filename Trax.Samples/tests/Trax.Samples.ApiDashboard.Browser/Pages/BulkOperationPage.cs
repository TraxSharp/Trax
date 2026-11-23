using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>One bulk operation: its status, its counts, why ids did not take, and the filter it ran over.</summary>
public sealed class BulkOperationPage(ConsolePage console)
    : AreaPage(console, "system/operations", "Bulk operation")
{
    public ILocator Counts => Page.GetByRole(AriaRole.Region, new() { Name = "Counts" });

    /// <summary>The count labelled <paramref name="label"/> (Matched, Done, Failed, Not attempted).</summary>
    public ILocator Count(string label) =>
        Counts.GetByRole(AriaRole.Group, new() { Name = label, Exact = true });

    public ILocator Filter => Page.GetByRole(AriaRole.Region, new() { Name = "Filter" });
}
