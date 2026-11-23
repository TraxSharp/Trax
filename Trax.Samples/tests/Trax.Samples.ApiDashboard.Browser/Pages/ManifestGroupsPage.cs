using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>Manifest groups, and the graph of which groups run after which.</summary>
public sealed class ManifestGroupsPage(ConsolePage console)
    : AreaPage(console, "schedules/groups", "Manifest groups")
{
    public ILocator Graph => Page.GetByRole(AriaRole.Group, new() { Name = "Dependency graph" });

    public ILocator NameFilter =>
        Page.GetByRole(AriaRole.Textbox, new() { Name = "Filter by name…", Exact = true });

    /// <summary>The node of the group named <paramref name="group"/>, which opens the group.</summary>
    public ILocator Node(string group) =>
        Graph.GetByRole(AriaRole.Button).Filter(new() { HasText = group });
}
