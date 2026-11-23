using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>Machines: every machine the host registers, with its instance and stuck counts.</summary>
public sealed class MachinesPage(ConsolePage console) : AreaPage(console, "machines", "Machines")
{
    public ILocator List => Page.GetByRole(AriaRole.Region, new() { Name = "Registered machines" });

    /// <summary>The row of one machine, by the link to its page.</summary>
    public ILocator Machine(string name) =>
        List.GetByRole(AriaRole.Row)
            .Filter(
                new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = true }) }
            );
}
