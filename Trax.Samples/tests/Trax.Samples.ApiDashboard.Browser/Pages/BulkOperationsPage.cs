using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>The bulk operations this host ran, under System.</summary>
public sealed class BulkOperationsPage(ConsolePage console)
    : AreaPage(console, "system/operations", "Bulk operations")
{
    public ILocator Operation(string title) => Console.RowLinked(title);
}
