using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>Hosts: every process that beats, what it runs, and the dashboard process's own stats.</summary>
public sealed class HostsPage(ConsolePage console) : AreaPage(console, "system/hosts", "Hosts")
{
    public ILocator Hosts => Page.GetByRole(AriaRole.Table, new() { Name = "Hosts", Exact = true });

    public ILocator ThisProcess =>
        Page.GetByRole(AriaRole.Region, new() { Name = "This dashboard's process", Exact = true });
}
