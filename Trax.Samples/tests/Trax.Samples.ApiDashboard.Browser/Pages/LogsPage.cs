using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>Every run's log entries, with filters held in the URL.</summary>
public sealed class LogsPage(ConsolePage console) : AreaPage(console, "system/logs", "Logs")
{
    public ILocator MinimumLevel => Page.GetByLabel("Minimum level", new() { Exact = true });
}
