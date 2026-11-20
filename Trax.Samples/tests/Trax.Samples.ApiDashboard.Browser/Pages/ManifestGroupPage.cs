using Microsoft.Playwright;
using Trax.Samples.ApiDashboard.Browser.Fixtures;

// One page object per area of the console. Each names its route and the heading that says it drew, and locates
// rows and controls by role and accessible name. A page item that changes an area's words or controls changes its
// page object with them, and the tests read the new words.
namespace Trax.Samples.ApiDashboard.Browser.Pages;

/// <summary>One manifest group: its stats, its settings, its manifests and runs, and Queue and Cancel.</summary>
public sealed class ManifestGroupPage(ConsolePage console, long id, string name)
    : AreaPage(console, $"schedules/groups/{id}", name)
{
    public ILocator Setting(string label) => Main.GetByLabel(label, new() { Exact = true });
}
