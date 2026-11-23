using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>
/// One machine instance: the machine drawn with its state lit, what it accepts and waits on, and its runs.
/// </summary>
public sealed class MachineInstancePage : AreaPage
{
    private readonly string machine;

    public MachineInstancePage(ConsolePage console, string machine, Guid id)
        : base(console, $"machines/{Uri.EscapeDataString(machine)}/system/{id}", machine) =>
        this.machine = machine;

    public ILocator Diagram =>
        Page.GetByRole(AriaRole.Group, new() { Name = $"States of {machine}", Exact = true });

    /// <summary>The state the instance is in, as the diagram lights it.</summary>
    public ILocator Lit => Diagram.Locator("[aria-current='step']");

    public ILocator WhereItIs => Page.GetByRole(AriaRole.Region, new() { Name = "Where it is" });

    /// <summary>The value the "Where it is" panel gives for <paramref name="label"/>.</summary>
    public ILocator Field(string label) =>
        WhereItIs.Locator($"xpath=.//dt[normalize-space()='{label}']/following-sibling::dd");

    public ILocator Commands =>
        Page.GetByRole(AriaRole.List, new() { Name = "Commands it accepts" });
}
