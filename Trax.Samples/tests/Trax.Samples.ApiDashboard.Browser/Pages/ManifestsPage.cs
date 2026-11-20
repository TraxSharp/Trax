using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>Manifests: what runs on a schedule, in words, when it runs next, and whether it is enabled.</summary>
public sealed class ManifestsPage(ConsolePage console) : AreaPage(console, "schedules", "Manifests")
{
    /// <summary>The tab of Schedules named <paramref name="name"/>.</summary>
    public ILocator Tab(string name) =>
        Page.GetByRole(AriaRole.Tablist, new() { Name = "Schedules" })
            .GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });
}
