using System.Globalization;
using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>The work queue: queued, staged and dispatched entries.</summary>
public sealed class WorkQueuePage(ConsolePage console) : AreaPage(console, "runs/queued", "Queued")
{
    /// <summary>The link a row gives to an entry, a run or a manifest by its id: <c>#id</c>.</summary>
    public ILocator IdLink(ILocator row, long id) =>
        row.GetByRole(
            AriaRole.Link,
            new() { Name = string.Create(CultureInfo.InvariantCulture, $"#{id}"), Exact = true }
        );
}
