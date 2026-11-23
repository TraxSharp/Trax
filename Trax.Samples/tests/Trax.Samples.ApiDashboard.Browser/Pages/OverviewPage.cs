using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>The Overview: what needs attention first, each entry a link to its list, then the charts.</summary>
public sealed class OverviewPage(ConsolePage console) : AreaPage(console, "", "Overview")
{
    public ILocator Attention =>
        Page.GetByRole(AriaRole.Region, new() { Name = "Needs attention", Exact = true });

    /// <summary>The attention entry whose text matches <paramref name="text"/>.</summary>
    public ILocator AttentionLink(System.Text.RegularExpressions.Regex text) =>
        Attention.GetByRole(AriaRole.Link, new() { NameRegex = text });

    /// <summary>A bar of a chart, by the name it carries as a link.</summary>
    public ILocator ChartBar(string name) =>
        Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true });
}
