using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>One queue entry's page: its fields, and what a queued entry waits on.</summary>
public sealed class WorkQueueEntryPage(ConsolePage console)
{
    public ILocator WaitingOn(long entry) =>
        console.Page.GetByRole(AriaRole.Link, new() { Name = $"Entry {entry}", Exact = true });
}
