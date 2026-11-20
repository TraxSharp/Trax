using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>
/// The tab strip every Runs list shows (All, Failed, Queued, Dead letters), the current view marked.
/// </summary>
public static class RunViews
{
    public static ILocator Strip(ConsolePage console) =>
        console.Page.GetByRole(AriaRole.Navigation, new() { Name = "Run views", Exact = true });

    public static ILocator Tab(ConsolePage console, string name) =>
        Strip(console).GetByRole(AriaRole.Link, new() { Name = name, Exact = true });
}
